using System.IO;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BotNC.App.Models;

namespace BotNC.App.Services;

public sealed class VisualRecognitionService(
    AppDatabase database,
    ScreenCaptureService capture)
{
    private const int ReferenceWidth = 1920;
    private const int ReferenceDesktopHeight = 1080;
    private const int ReferenceWindowHeight = 1040;
    private static readonly ConditionalWeakTable<PixelFrame, PixelFrame> NormalizedFrames = new();
    private static readonly ConditionalWeakTable<byte[], PixelFrame> DecodedReferences = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<VisualReference>>> _referenceCache = new();

    private Task<VisualReference> GetReferenceAsync(string referenceId) =>
        referenceId.StartsWith("abadia_", StringComparison.Ordinal)
            ? _referenceCache.GetOrAdd(
                referenceId,
                id => new Lazy<Task<VisualReference>>(() => database.GetReferenceAsync(id))).Value
            : database.GetReferenceAsync(referenceId);
    public async Task<RecognitionResult> FindAsync(
        string referenceId,
        CancellationToken cancellationToken = default)
    {
        var reference = await GetReferenceAsync(referenceId);
        var screen = capture.CapturePrimaryScreen();
        return await Task.Run(
            () => Match(screen, reference, cancellationToken),
            cancellationToken);
    }

    public async Task<RecognitionResult> FindAsync(
        string referenceId,
        PixelFrame frame,
        CancellationToken cancellationToken = default)
    {
        var reference = await GetReferenceAsync(referenceId);
        return await Task.Run(
            () => Match(frame, reference, cancellationToken),
            cancellationToken);
    }

    public async Task<RecognitionResult> FindAsync(
        string referenceId,
        int searchX,
        int searchY,
        int searchWidth,
        int searchHeight,
        CancellationToken cancellationToken = default)
    {
        var reference = await GetReferenceAsync(referenceId);
        var regionalReference = reference with
        {
            SearchX = searchX,
            SearchY = searchY,
            SearchWidth = searchWidth,
            SearchHeight = searchHeight
        };
        var screen = capture.CapturePrimaryScreen();
        return await Task.Run(
            () => Match(screen, regionalReference, cancellationToken),
            cancellationToken);
    }

    public async Task<RecognitionResult> FindInImageAsync(
        string referenceId,
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        var reference = await GetReferenceAsync(referenceId);
        var image = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        var frame = Decode(image);
        return await Task.Run(
            () => Match(frame, reference, cancellationToken),
            cancellationToken);
    }

    public async Task<RecognitionResult> FindInCroppedImageAsync(
        string referenceId,
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        var reference = await GetReferenceAsync(referenceId);
        var image = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        var frame = Decode(image);
        var localReference = reference with
        {
            SearchX = 0,
            SearchY = 0,
            SearchWidth = frame.Width,
            SearchHeight = frame.Height
        };
        return await Task.Run(
            () => Match(frame, localReference, cancellationToken, normalizeScreen: false),
            cancellationToken);
    }

    public async Task<double> MeasureAverageLumaInImageAsync(
        string imagePath,
        int x,
        int y,
        int width,
        int height,
        CancellationToken cancellationToken = default)
    {
        var image = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        return MeasureAverageLuma(Decode(image), x, y, width, height);
    }

    public async Task<bool> HasDailyThirtyCounterInImageAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        var image = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        return HasDailyThirtyCounter(Decode(image));
    }

    public async Task<int> CountDimmedDailyMissionRowsInImageAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        var image = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        return CountDimmedDailyMissionRows(Decode(image));
    }

    public async Task<bool> HasUnclaimedServerMailInImageAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        var image = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        return HasUnclaimedServerMail(Decode(image));
    }

    public static bool HasUnclaimedServerMail(PixelFrame frame)
    {
        if (frame.Width < 1890 || frame.Height < 500)
        {
            return false;
        }

        var redPixels = 0;
        for (var y = 180; y < 490; y++)
        {
            for (var x = 1850; x < 1888; x++)
            {
                var offset = (y * frame.Stride) + (x * 4);
                var blue = frame.Pixels[offset];
                var green = frame.Pixels[offset + 1];
                var red = frame.Pixels[offset + 2];
                if (red >= 150 && green <= 105 && blue <= 110 && red >= green + 65)
                {
                    redPixels++;
                    if (redPixels >= 25)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public static bool HasDailyThirtyCounter(PixelFrame frame)
    {
        // O contador 30/30 fica vermelho quando o limite diário foi atingido;
        // em 0/30 (e nos valores intermediários) o texto é cinza. A cor elimina
        // o falso positivo que a semelhança estrutural entre "0/30" e "30/30"
        // produzia no template anterior.
        if (frame.Width < 590 || frame.Height < 1010)
        {
            return false;
        }

        var redPixels = 0;
        for (var y = 955; y < 1010; y++)
        {
            for (var x = 470; x < 590; x++)
            {
                var index = (y * frame.Stride) + (x * 4);
                var blue = frame.Pixels[index];
                var green = frame.Pixels[index + 1];
                var red = frame.Pixels[index + 2];
                if (red >= 160 && red >= green + 40 && blue <= 90)
                {
                    redPixels++;
                    if (redPixels >= 100)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public static int CountDimmedDailyMissionRows(PixelFrame frame)
    {
        var rows = new List<(int Y, int Count)>();
        for (var y = 250; y < Math.Min(920, frame.Height); y++)
        {
            var count = 0;
            for (var x = 1480; x < Math.Min(1880, frame.Width); x++)
            {
                var index = (y * frame.Stride) + (x * 4);
                var blue = frame.Pixels[index];
                var green = frame.Pixels[index + 1];
                var red = frame.Pixels[index + 2];
                if (red >= 45 && blue >= 55 && green <= 125 &&
                    red >= green + 5 && blue >= green + 12 && blue >= red + 4)
                {
                    count++;
                }
            }

            if (count >= 16)
            {
                rows.Add((y, count));
            }
        }

        if (rows.Count == 0)
        {
            return 0;
        }

        var clusterCount = 0;
        var clusterStrength = 0;
        var lastY = -10;
        foreach (var row in rows)
        {
            if (row.Y - lastY > 3)
            {
                if (clusterStrength >= 80)
                {
                    clusterCount++;
                }
                clusterStrength = 0;
            }

            clusterStrength += row.Count;
            lastY = row.Y;
        }

        return clusterStrength >= 80 ? clusterCount + 1 : clusterCount;
    }

    public static double MeasureAverageLuma(
        PixelFrame frame,
        int x,
        int y,
        int width,
        int height)
    {
        var left = Math.Clamp(x, 0, frame.Width - 1);
        var top = Math.Clamp(y, 0, frame.Height - 1);
        var right = Math.Clamp(x + width, left + 1, frame.Width);
        var bottom = Math.Clamp(y + height, top + 1, frame.Height);
        double total = 0;
        var count = 0;
        for (var sampleY = top; sampleY < bottom; sampleY += 2)
        {
            for (var sampleX = left; sampleX < right; sampleX += 2)
            {
                total += ReadLuma(frame, sampleX, sampleY);
                count++;
            }
        }

        return count == 0 ? 0 : total / count;
    }

    public async Task<string> SaveDiagnosticAsync(string name)
    {
        var frame = capture.CapturePrimaryScreen();
        return await SaveDiagnosticAsync(name, frame);
    }

    public async Task<string> SaveDiagnosticAsync(string name, PixelFrame frame)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppIdentity.DataDirectoryName,
            "Diagnosticos");
        Directory.CreateDirectory(directory);
        var safeName = string.Concat(name.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '-' : character));
        var path = Path.Combine(directory, $"{DateTime.Now:yyyyMMdd-HHmmss}-{safeName}.png");
        var bitmap = BitmapSource.Create(
            frame.Width,
            frame.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            frame.Pixels,
            frame.Stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        await using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    private static RecognitionResult Match(
        PixelFrame screen,
        VisualReference reference,
        CancellationToken cancellationToken,
        bool normalizeScreen = true)
    {
        if (normalizeScreen)
        {
            screen = NormalizeForReferenceMatching(screen);
        }
        var template = reference.Id.StartsWith("abadia_", StringComparison.Ordinal)
            ? DecodedReferences.GetValue(reference.Image, Decode)
            : Decode(reference.Image);
        ValidateSource(reference, template);

        var sourceX = reference.SourceX;
        var sourceY = reference.SourceY;
        var templateWidth = reference.SourceWidth;
        var templateHeight = reference.SourceHeight;
        var searchX = Math.Clamp(reference.SearchX, 0, screen.Width - 1);
        var searchY = Math.Clamp(reference.SearchY, 0, screen.Height - 1);
        var searchRight = Math.Min(screen.Width, reference.SearchX + reference.SearchWidth);
        var searchBottom = Math.Min(screen.Height, reference.SearchY + reference.SearchHeight);
        var maximumX = searchRight - templateWidth;
        var maximumY = searchBottom - templateHeight;
        if (maximumX < searchX || maximumY < searchY)
        {
            return new RecognitionResult(false, 0, 0, 0);
        }

        var sampleStride = Math.Max(2, Math.Min(templateWidth, templateHeight) / 36);
        var samples = BuildSamples(
            template,
            sourceX,
            sourceY,
            templateWidth,
            templateHeight,
            sampleStride);
        var templateMean = samples.Average(sample => sample.Luma);
        var templateVariance = samples.Sum(sample =>
            Square(sample.Luma - templateMean));
        if (templateVariance < 0.001)
        {
            return new RecognitionResult(false, 0, 0, 0);
        }

        var positionStep = templateWidth > 400 || templateHeight > 300 ? 3 : 2;
        var bestScore = double.NegativeInfinity;
        var bestX = 0;
        var bestY = 0;

        for (var y = searchY; y <= maximumY; y += positionStep)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = searchX; x <= maximumX; x += positionStep)
            {
                var candidateMean = 0d;
                foreach (var sample in samples)
                {
                    candidateMean += ReadLuma(screen, x + sample.X, y + sample.Y);
                }

                candidateMean /= samples.Length;
                var covariance = 0d;
                var candidateVariance = 0d;
                foreach (var sample in samples)
                {
                    var templateDelta = sample.Luma - templateMean;
                    var candidateDelta = ReadLuma(screen, x + sample.X, y + sample.Y) - candidateMean;
                    covariance += templateDelta * candidateDelta;
                    candidateVariance += candidateDelta * candidateDelta;
                }

                var denominator = Math.Sqrt(templateVariance * candidateVariance);
                var score = denominator < 0.001 ? -1 : covariance / denominator;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestX = x;
                    bestY = y;
                }
            }
        }

        return new RecognitionResult(
            bestScore >= reference.Threshold,
            Math.Clamp(bestScore, 0, 1),
            bestX + (templateWidth / 2),
            bestY + (templateHeight / 2));
    }

    private static TemplateSample[] BuildSamples(
        PixelFrame template,
        int sourceX,
        int sourceY,
        int width,
        int height,
        int stride)
    {
        var samples = new List<TemplateSample>();
        for (var y = stride / 2; y < height; y += stride)
        {
            for (var x = stride / 2; x < width; x += stride)
            {
                samples.Add(new TemplateSample(
                    x,
                    y,
                    ReadLuma(template, sourceX + x, sourceY + y)));
            }
        }

        return [.. samples];
    }

    private static PixelFrame Decode(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        var decoder = new PngBitmapDecoder(
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
        return new PixelFrame(converted.PixelWidth, converted.PixelHeight, stride, pixels);
    }

    private static PixelFrame NormalizeForReferenceMatching(PixelFrame frame)
    {
        if (Math.Abs(frame.Width - ReferenceWidth) <= 2 &&
            (Math.Abs(frame.Height - ReferenceDesktopHeight) <= 2 ||
             Math.Abs(frame.Height - ReferenceWindowHeight) <= 2))
        {
            return frame;
        }

        if (frame.Width < 640 || frame.Height < 360)
        {
            return frame;
        }

        return NormalizedFrames.GetValue(
            frame,
            source =>
            {
                var aspect = source.Width / (double)source.Height;
                var desktopDistance = Math.Abs(aspect - (ReferenceWidth / (double)ReferenceDesktopHeight));
                var windowDistance = Math.Abs(aspect - (ReferenceWidth / (double)ReferenceWindowHeight));
                var targetHeight = windowDistance < desktopDistance
                    ? ReferenceWindowHeight
                    : ReferenceDesktopHeight;
                return ResizeFrame(source, ReferenceWidth, targetHeight);
            });
    }

    private static PixelFrame ResizeFrame(PixelFrame source, int width, int height)
    {
        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        var xScale = source.Width / (double)width;
        var yScale = source.Height / (double)height;
        for (var y = 0; y < height; y++)
        {
            var sourceY = Math.Min(source.Height - 1, (int)((y + 0.5) * yScale));
            var sourceRow = sourceY * source.Stride;
            var targetRow = y * stride;
            for (var x = 0; x < width; x++)
            {
                var sourceX = Math.Min(source.Width - 1, (int)((x + 0.5) * xScale));
                var sourceIndex = sourceRow + (sourceX * 4);
                var targetIndex = targetRow + (x * 4);
                pixels[targetIndex] = source.Pixels[sourceIndex];
                pixels[targetIndex + 1] = source.Pixels[sourceIndex + 1];
                pixels[targetIndex + 2] = source.Pixels[sourceIndex + 2];
                pixels[targetIndex + 3] = source.Pixels[sourceIndex + 3];
            }
        }

        return new PixelFrame(width, height, stride, pixels);
    }

    private static void ValidateSource(VisualReference reference, PixelFrame template)
    {
        if (reference.SourceX < 0 || reference.SourceY < 0 ||
            reference.SourceWidth <= 0 || reference.SourceHeight <= 0 ||
            reference.SourceX + reference.SourceWidth > template.Width ||
            reference.SourceY + reference.SourceHeight > template.Height)
        {
            throw new InvalidOperationException(
                $"A região da referência {reference.Id} está fora da imagem cadastrada.");
        }
    }

    private static double ReadLuma(PixelFrame frame, int x, int y)
    {
        var index = (y * frame.Stride) + (x * 4);
        var blue = frame.Pixels[index];
        var green = frame.Pixels[index + 1];
        var red = frame.Pixels[index + 2];
        return (red * 0.299) + (green * 0.587) + (blue * 0.114);
    }

    private static double Square(double value) => value * value;

    private sealed record TemplateSample(int X, int Y, double Luma);
}
