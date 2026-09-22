using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace BotNC.App.Services;

internal sealed class ActivationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HttpClient Client = CreateHttpClient();
    private readonly string _directory =
        string.Equals(Environment.GetEnvironmentVariable("PEXBOT_LICENSE_ALLOW_UNPINNED_DEV"), "1", StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PEXBOT_ACTIVATION_TEST_DIR"))
            ? Environment.GetEnvironmentVariable("PEXBOT_ACTIVATION_TEST_DIR")!
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PEXBOT", "Activation");
    private readonly Uri? _server;
    private readonly string? _publicKey;

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        // Evita a página informativa do túnel gratuito; o servidor ainda valida
        // credenciais, HWID e assinatura normalmente.
        client.DefaultRequestHeaders.TryAddWithoutValidation("ngrok-skip-browser-warning", "pexbot-client");
        return client;
    }

    public ActivationService()
    {
        var configured = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "LicenseServerUrl")?.Value;
        _publicKey = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "LicensePublicKey")?.Value;
        // A configuração de desenvolvimento não pode desligar a exigência de um build distribuído.
        if (string.IsNullOrWhiteSpace(configured) &&
            string.Equals(Environment.GetEnvironmentVariable("PEXBOT_LICENSE_ALLOW_UNPINNED_DEV"), "1", StringComparison.Ordinal))
            configured = Environment.GetEnvironmentVariable("PEXBOT_LICENSE_URL");
        if (string.IsNullOrWhiteSpace(configured)) return;
        if (string.IsNullOrWhiteSpace(_publicKey) &&
            !string.Equals(Environment.GetEnvironmentVariable("PEXBOT_LICENSE_ALLOW_UNPINNED_DEV"), "1", StringComparison.Ordinal))
            throw new InvalidOperationException("O build exige a chave pública do servidor de login.");
        if (!Uri.TryCreate(configured.TrimEnd('/'), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps &&
             !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            throw new InvalidOperationException("O endereço do servidor de login deve usar HTTPS.");
        _server = uri;
    }

    public bool IsRequired => _server is not null;

    public bool HasValidActivation()
    {
        if (!IsRequired) return true;
        try
        {
            var device = ReadDevice();
            var protectedLicense = File.ReadAllBytes(Path.Combine(_directory, "license.dat"));
            var json = ProtectedData.Unprotect(protectedLicense, null, DataProtectionScope.CurrentUser);
            var saved = JsonSerializer.Deserialize<SavedLicense>(json, JsonOptions);
            if (saved is null) return false;
            var payload = Convert.FromBase64String(saved.Payload);
            var signature = Convert.FromBase64String(saved.Signature);
            using var verifier = ECDsa.Create();
            if (!IsExpectedPublicKey(saved.PublicKey)) return false;
            verifier.ImportSubjectPublicKeyInfo(Convert.FromBase64String(saved.PublicKey), out _);
            if (!verifier.VerifyData(payload, signature, HashAlgorithmName.SHA256)) return false;
            var license = JsonSerializer.Deserialize<OfflineLicense>(payload, JsonOptions);
            return license is { Version: 1 } &&
                   string.Equals(license.DeviceId, device.DeviceId, StringComparison.Ordinal) &&
                   string.Equals(license.Fingerprint, device.Fingerprint, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
               CryptographicException or JsonException or FormatException)
        {
            return false;
        }
    }

    public async Task ActivateAsync(string username, string password, CancellationToken cancellationToken)
    {
        if (_server is null) throw new InvalidOperationException("Servidor de login não configurado.");
        var device = ReadOrCreateDevice();
        var endpoint = new Uri(_server, "/api/activate");
        using var response = await Client.PostAsJsonAsync(endpoint,
            new ActivationRequest(username, password, device.DeviceId, device.Fingerprint, Environment.MachineName),
            JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            try
            {
                var message = JsonDocument.Parse(error).RootElement.GetProperty("error").GetString();
                throw new InvalidOperationException(message ?? "Login recusado.");
            }
            catch (JsonException)
            {
                throw new InvalidOperationException($"Não foi possível ativar o PEXBOT (HTTP {(int)response.StatusCode}).");
            }
        }

        var received = await response.Content.ReadFromJsonAsync<ActivationResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Resposta vazia do servidor de login.");
        var payload = Convert.FromBase64String(received.Payload);
        if (!IsExpectedPublicKey(received.PublicKey))
            throw new CryptographicException("Chave do servidor de login diferente da chave esperada.");
        using var verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(Convert.FromBase64String(received.PublicKey), out _);
        if (!verifier.VerifyData(payload, Convert.FromBase64String(received.Signature), HashAlgorithmName.SHA256))
            throw new CryptographicException("A autorização recebida não tem assinatura válida.");
        var license = JsonSerializer.Deserialize<OfflineLicense>(payload, JsonOptions);
        if (license is not { Version: 1 } ||
            !string.Equals(license.DeviceId, device.DeviceId, StringComparison.Ordinal) ||
            !string.Equals(license.Fingerprint, device.Fingerprint, StringComparison.Ordinal))
            throw new CryptographicException("A autorização recebida não corresponde a este computador.");

        Directory.CreateDirectory(_directory);
        var saved = JsonSerializer.SerializeToUtf8Bytes(received, JsonOptions);
        var encrypted = ProtectedData.Protect(saved, null, DataProtectionScope.CurrentUser);
        var path = Path.Combine(_directory, "license.dat");
        var temporary = path + ".new";
        await File.WriteAllBytesAsync(temporary, encrypted, cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    private DeviceIdentity ReadOrCreateDevice()
    {
        if (File.Exists(Path.Combine(_directory, "device.dat"))) return ReadDevice();
        Directory.CreateDirectory(_directory);
        var device = new DeviceIdentity(Guid.NewGuid().ToString("N"), ReadFingerprint());
        var encoded = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(device, JsonOptions),
            null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(Path.Combine(_directory, "device.dat"), encoded);
        return device;
    }

    private DeviceIdentity ReadDevice()
    {
        var path = Path.Combine(_directory, "device.dat");
        var encoded = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
        var saved = JsonSerializer.Deserialize<DeviceIdentity>(encoded, JsonOptions)
            ?? throw new InvalidDataException("Identidade local inválida.");
        return saved with { Fingerprint = ReadFingerprint() };
    }

    private static string ReadFingerprint()
    {
        using var registry = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        var machineGuid = registry?.GetValue("MachineGuid") as string;
        if (string.IsNullOrWhiteSpace(machineGuid))
            throw new InvalidOperationException("Não foi possível identificar este computador.");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(machineGuid))).ToLowerInvariant();
    }

    private bool IsExpectedPublicKey(string received) =>
        string.IsNullOrWhiteSpace(_publicKey) ||
        string.Equals(received, _publicKey, StringComparison.Ordinal);

    private sealed record DeviceIdentity(string DeviceId, string Fingerprint);
    private sealed record ActivationRequest(string Username, string Password, string DeviceId, string Fingerprint, string MachineName);
    private sealed record OfflineLicense(int Version, string Username, string DeviceId, string Fingerprint, DateTimeOffset IssuedAtUtc);
    private sealed record ActivationResponse(string Payload, string Signature, string PublicKey);
    private sealed record SavedLicense(string Payload, string Signature, string PublicKey);
}
