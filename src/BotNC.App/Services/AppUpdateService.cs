using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BotNC.App.Services;

public sealed record AvailableUpdate(Version Version, string InstallerUrl, string ChecksumUrl);

public sealed class AppUpdateService
{
    private const string Repository = "lipex15/ncbotz";
    private static readonly Uri LatestReleaseUri = new($"https://api.github.com/repos/{Repository}/releases/latest");
    private static readonly HttpClient Client = CreateClient();

    public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);

    public async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(LatestReleaseUri, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var release = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = release.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version))
        {
            throw new InvalidDataException("A versão publicada no GitHub não tem um número válido.");
        }

        if (version <= CurrentVersion)
        {
            return null;
        }

        var baseName = $"PEXBOT-Setup-v{version.ToString(3)}";
        var installer = FindAsset(root, baseName + ".exe");
        var checksum = FindAsset(root, baseName + ".sha256");
        if (installer is null || checksum is null)
        {
            throw new InvalidDataException("A versão nova ainda não contém o instalador e a verificação de integridade.");
        }

        return new AvailableUpdate(version, installer, checksum);
    }

    public async Task<string> DownloadAndVerifyAsync(
        AvailableUpdate update,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        var expectedName = $"PEXBOT-Setup-v{update.Version.ToString(3)}.exe";
        var expectedHash = await ReadExpectedHashAsync(update.ChecksumUrl, cancellationToken);
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PEXBOT",
            "Updates");
        Directory.CreateDirectory(directory);
        var completedPath = Path.Combine(directory, expectedName);
        var temporaryPath = completedPath + ".download";

        try
        {
            using var response = await Client.GetAsync(
                ValidateReleaseUrl(update.InstallerUrl),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            var contentLength = response.Content.Headers.ContentLength;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(
                temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
            {
                var buffer = new byte[1024 * 1024];
                long downloaded = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    downloaded += count;
                    if (contentLength is > 0)
                    {
                        progress.Report((int)Math.Clamp(downloaded * 100 / contentLength.Value, 0, 100));
                    }
                }
            }

            string actualHash;
            await using (var downloadedFile = File.OpenRead(temporaryPath))
            {
                actualHash = Convert.ToHexString(await SHA256.HashDataAsync(downloadedFile, cancellationToken));
            }
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("A verificação de integridade do instalador falhou. O download foi descartado.");
            }

            File.Move(temporaryPath, completedPath, overwrite: true);
            progress.Report(100);
            return completedPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static void LaunchSilentUpdate(string verifiedInstallerPath)
    {
        if (!File.Exists(verifiedInstallerPath))
        {
            throw new FileNotFoundException("O instalador verificado não foi encontrado.", verifiedInstallerPath);
        }

        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PEXBOT",
            "Logs");
        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(logDirectory, "update-install.log");

        _ = Process.Start(new ProcessStartInfo
        {
            FileName = verifiedInstallerPath,
            Arguments =
                $"/SP- /VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART " +
                $"/RESTARTAPP=1 /LOG=\"{logPath}\"",
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("O Windows não conseguiu iniciar a atualização silenciosa.");
    }

    private static async Task<string> ReadExpectedHashAsync(string checksumUrl, CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(ValidateReleaseUrl(checksumUrl), cancellationToken);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        var match = Regex.Match(text.Trim(), "^[0-9a-fA-F]{64}(?=\\s|$)");
        if (!match.Success)
        {
            throw new InvalidDataException("O arquivo de integridade da atualização é inválido.");
        }

        return match.Value;
    }

    private static string? FindAsset(JsonElement release, string expectedName)
    {
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() == expectedName)
            {
                return asset.GetProperty("browser_download_url").GetString();
            }
        }

        return null;
    }

    private static Uri ValidateReleaseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith($"/{Repository}/releases/download/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("A atualização não aponta para uma versão oficial do repositório.");
        }

        return uri;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PEXBOT", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
