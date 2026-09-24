namespace BotNC.App.Services;

/// <summary>One coordinate contract for all testing-channel readers and input.
/// References include the original 23 px title bar, but never the taskbar.
/// The live client rectangle is measured, not inferred from desktop resolution.</summary>
internal readonly record struct ReferenceViewport(int Left, int Top, int Width, int Height)
{
    public const int ReferenceWidth = 1920;
    public const int ReferenceHeight = 1040;
    public const int TitleHeight = 23;
    public const int ContentHeight = ReferenceHeight - TitleHeight;
    public double ScaleX => Width / (double)ReferenceWidth;
    public double ScaleY => Height / (double)ContentHeight;

    public static bool IsSupportedDisplay(int width, int height) =>
        (width, height) is (1920, 1080) ||
        AppIdentity.IsTesting && ((width, height) is (1366, 768) or (1440, 900));

    public static string SupportedDisplays => AppIdentity.IsTesting
        ? "1920×1080, 1366×768 ou 1440×900" : "1920×1080";

    public (int X, int Y) ToScreen(int x, int y)
    {
        Validate();
        if (x < 0 || x >= ReferenceWidth || y < TitleHeight || y >= ReferenceHeight)
            throw new InvalidOperationException($"Coordenada fora da área do jogo: {x},{y}.");
        return (Left + Math.Clamp((int)Math.Round(x * ScaleX), 0, Width - 1),
            Top + Math.Clamp((int)Math.Round((y - TitleHeight) * ScaleY), 0, Height - 1));
    }

    public (int X, int Y) ToReference(int screenX, int screenY)
    {
        Validate();
        if (screenX < Left || screenX >= Left + Width || screenY < Top || screenY >= Top + Height)
            throw new InvalidOperationException("O ponto precisa estar dentro da área do jogo selecionado.");
        return (Math.Clamp((int)Math.Round((screenX - Left) / ScaleX), 0, ReferenceWidth - 1),
            Math.Clamp(TitleHeight + (int)Math.Round((screenY - Top) / ScaleY), TitleHeight, ReferenceHeight - 1));
    }

    public void Validate()
    {
        if (Width < 640 || Height < 360)
            throw new InvalidOperationException($"Área do jogo inválida: {Width}×{Height}.");
    }

    // Crop in WGC coordinates, resize ONLY the game, then restore the reference
    // title offset. Windows decorations must not scale with the game's UI.
    public PixelFrame Normalize(PixelFrame source, int cropX, int cropY)
    {
        Validate();
        if (cropX < 0 || cropY < 0 || cropX + Width > source.Width || cropY + Height > source.Height)
            throw new InvalidOperationException("A captura não corresponde à geometria atual da janela; aguardando novo quadro.");
        var content = new PixelFrame(Width, Height, Width * 4, new byte[Width * Height * 4]);
        for (var row = 0; row < Height; row++)
            Buffer.BlockCopy(source.Pixels, (row + cropY) * source.Stride + cropX * 4,
                content.Pixels, row * content.Stride, content.Stride);
        var scaled = Resize(content, ReferenceWidth, ContentHeight);
        var normalized = new PixelFrame(ReferenceWidth, ReferenceHeight, ReferenceWidth * 4,
            new byte[ReferenceWidth * ReferenceHeight * 4]);
        Buffer.BlockCopy(scaled.Pixels, 0, normalized.Pixels, TitleHeight * normalized.Stride, scaled.Pixels.Length);
        return normalized with { NativeContent = content, Viewport = this };
    }

    internal static PixelFrame Resize(PixelFrame source, int width, int height)
    {
        if (source.Width == width && source.Height == height) return source;
        var pixels = new byte[checked(width * height * 4)];
        // Bilinear interpolation preserves strokes much better than nearest-neighbour
        // when normalizing smaller UI fonts for the existing pixel/OCR readers.
        for (var y = 0; y < height; y++)
        {
            var sy = Math.Clamp((y + .5) * source.Height / height - .5, 0, source.Height - 1);
            var y0 = (int)sy; var y1 = Math.Min(y0 + 1, source.Height - 1); var fy = sy - y0;
            for (var x = 0; x < width; x++)
            {
                var sx = Math.Clamp((x + .5) * source.Width / width - .5, 0, source.Width - 1);
                var x0 = (int)sx; var x1 = Math.Min(x0 + 1, source.Width - 1); var fx = sx - x0;
                for (var c = 0; c < 4; c++)
                {
                    var top = source.Pixels[y0 * source.Stride + x0 * 4 + c] * (1 - fx) + source.Pixels[y0 * source.Stride + x1 * 4 + c] * fx;
                    var bottom = source.Pixels[y1 * source.Stride + x0 * 4 + c] * (1 - fx) + source.Pixels[y1 * source.Stride + x1 * 4 + c] * fx;
                    pixels[(y * width + x) * 4 + c] = (byte)Math.Round(top * (1 - fy) + bottom * fy);
                }
            }
        }
        return new PixelFrame(width, height, width * 4, pixels);
    }
}
