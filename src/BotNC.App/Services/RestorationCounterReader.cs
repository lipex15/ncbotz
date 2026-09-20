using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace BotNC.App.Services;

public enum RestorationTab
{
    Unknown,
    Experience,
    Equipment
}

public enum RestorationCountState
{
    Unknown,
    Empty,
    Pending
}

public sealed record RestorationCounterResult(
    RestorationTab Tab,
    int? Count,
    int? Capacity,
    string RawText)
{
    public RestorationCountState State => Count switch
    {
        0 when Tab != RestorationTab.Unknown => RestorationCountState.Empty,
        > 0 when Tab != RestorationTab.Unknown => RestorationCountState.Pending,
        _ => RestorationCountState.Unknown
    };
}

/// <summary>
/// Reads the resource count from the heading of the death-restoration panel.
/// An unreadable heading is Unknown, never an empty list. The caller must first
/// confirm that the restoration panel is open and capture the correct client.
/// </summary>
public sealed partial class RestorationCounterReader
{
    private const int ReferenceWidth = 1920;
    private const int ReferenceHeight = 1040;

    public Task<RestorationCounterResult> ReadClientFrameAsync(
        PixelFrame frame,
        CancellationToken cancellationToken = default) =>
        ReadHeaderCropAsync(CropHeading(frame, isFullClientFrame: true), cancellationToken);

    public async Task<RestorationCounterResult> ReadImageAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(imagePath);
        var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(
            decoder.Frames[0],
            PixelFormats.Bgra32,
            null,
            0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var frame = new PixelFrame(converted.PixelWidth, converted.PixelHeight, stride, pixels);
        return await ReadHeaderCropAsync(
            CropHeading(frame, isFullClientFrame: frame.Width >= 1000),
            cancellationToken);
    }

    public async Task<RestorationCounterResult> ReadHeaderCropAsync(
        PixelFrame heading,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var engine = OcrEngine.TryCreateFromUserProfileLanguages() ??
                     OcrEngine.TryCreateFromLanguage(new Language("pt-BR")) ??
                     OcrEngine.TryCreateFromLanguage(new Language("en-US")) ??
                     throw new InvalidOperationException("O reconhecimento de texto do Windows não está disponível.");

        var observations = new List<RestorationCounterResult>();
        foreach (var threshold in new int?[] { null, 105, 145 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var bitmap = MakeOcrBitmap(heading, threshold);
            var ocr = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken);
            observations.Add(Parse(ocr.Text));
        }

        var recognized = observations
            .Where(observation => observation.State != RestorationCountState.Unknown)
            .ToArray();
        var rawText = string.Join(" | ", observations.Select(observation => observation.RawText));
        if (recognized.Length == 0 ||
            recognized.Any(observation => observation.Tab != recognized[0].Tab ||
                                          observation.Count != recognized[0].Count))
        {
            return new RestorationCounterResult(RestorationTab.Unknown, null, null, rawText);
        }

        return recognized[0] with { RawText = rawText };
    }

    private static RestorationCounterResult Parse(string rawText)
    {
        var normalized = Normalize(rawText);
        var experience = ExperienceHeading().Match(normalized);
        if (experience.Success &&
            int.TryParse(experience.Groups["count"].Value, out var experienceCount) &&
            int.TryParse(experience.Groups["capacity"].Value, out var capacity) &&
            capacity is >= 1 and <= 20 && experienceCount <= capacity)
        {
            return new RestorationCounterResult(
                RestorationTab.Experience,
                experienceCount,
                capacity,
                normalized);
        }

        var equipment = EquipmentHeading().Match(normalized);
        if (equipment.Success &&
            int.TryParse(equipment.Groups["count"].Value, out var equipmentCount) &&
            equipmentCount is >= 0 and <= 50)
        {
            return new RestorationCounterResult(
                RestorationTab.Equipment,
                equipmentCount,
                null,
                normalized);
        }

        return new RestorationCounterResult(RestorationTab.Unknown, null, null, normalized);
    }

    private static PixelFrame CropHeading(PixelFrame frame, bool isFullClientFrame)
    {
        var scaleX = isFullClientFrame ? frame.Width / (double)ReferenceWidth : 1d;
        var scaleY = isFullClientFrame ? frame.Height / (double)ReferenceHeight : 1d;
        var x = Math.Clamp((int)Math.Round(80 * scaleX), 0, frame.Width - 1);
        var y = Math.Clamp((int)Math.Round(65 * scaleY), 0, frame.Height - 1);
        var width = Math.Min((int)Math.Round(390 * scaleX), frame.Width - x);
        var height = Math.Min((int)Math.Round(100 * scaleY), frame.Height - y);
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var row = 0; row < height; row++)
        {
            Buffer.BlockCopy(
                frame.Pixels,
                ((y + row) * frame.Stride) + (x * 4),
                pixels,
                row * stride,
                stride);
        }

        return new PixelFrame(width, height, stride, pixels);
    }

    private static SoftwareBitmap MakeOcrBitmap(PixelFrame frame, int? threshold)
    {
        const int enlargement = 3;
        var width = frame.Width * enlargement;
        var height = frame.Height * enlargement;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var sourceY = y / enlargement;
            for (var x = 0; x < width; x++)
            {
                var sourceX = x / enlargement;
                var sourceOffset = (sourceY * frame.Stride) + (sourceX * 4);
                var destinationOffset = (y * stride) + (x * 4);
                var blue = frame.Pixels[sourceOffset];
                var green = frame.Pixels[sourceOffset + 1];
                var red = frame.Pixels[sourceOffset + 2];
                var luma = ((red * 77) + (green * 150) + (blue * 29)) >> 8;
                var value = threshold is null
                    ? (byte)luma
                    : luma >= threshold ? (byte)255 : (byte)0;
                pixels[destinationOffset] = value;
                pixels[destinationOffset + 1] = value;
                pixels[destinationOffset + 2] = value;
                pixels[destinationOffset + 3] = 255;
            }
        }

        var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
        bitmap.CopyFromBuffer(CryptographicBuffer.CreateFromByteArray(pixels));
        return bitmap;
    }

    private static string Normalize(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var withoutMarks = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) !=
                System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                withoutMarks.Append(character);
            }
        }

        return Regex.Replace(withoutMarks.ToString(), @"\s+", " ").Trim().ToUpperInvariant();
    }

    [GeneratedRegex(@"PERDA\s+DE\s+EXP\s*(?<count>\d{1,2})\s*/\s*(?<capacity>\d{1,2})", RegexOptions.CultureInvariant)]
    private static partial Regex ExperienceHeading();

    [GeneratedRegex(@"EQUIPAMENTO\s+DANIFICADO\s*(?<count>\d{1,2})", RegexOptions.CultureInvariant)]
    private static partial Regex EquipmentHeading();
}
