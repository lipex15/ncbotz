using System.Runtime.InteropServices;

namespace BotNC.App.Services;

public sealed record PixelFrame(int Width, int Height, int Stride, byte[] Pixels);

public sealed class ScreenCaptureService
{
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;
    private const uint Srccopy = 0x00CC0020;
    private const uint DibRgbColors = 0;

    public (int Width, int Height) GetPrimaryScreenSize() =>
        (NativeMethods.GetSystemMetrics(SmCxScreen), NativeMethods.GetSystemMetrics(SmCyScreen));

    public PixelFrame CapturePrimaryScreen()
    {
        var (width, height) = GetPrimaryScreenSize();
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("Não foi possível medir o monitor principal.");
        }

        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        var memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
        var bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, width, height);
        var previous = NativeMethods.SelectObject(memoryDc, bitmap);

        try
        {
            if (!NativeMethods.BitBlt(memoryDc, 0, 0, width, height, screenDc, 0, 0, Srccopy))
            {
                throw new InvalidOperationException("A captura visual do monitor principal falhou.");
            }

            var stride = width * 4;
            var pixels = new byte[stride * height];
            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = checked((uint)Marshal.SizeOf<BitmapInfoHeader>()),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0,
                    SizeImage = checked((uint)pixels.Length)
                }
            };
            var copied = NativeMethods.GetDIBits(
                memoryDc,
                bitmap,
                0,
                checked((uint)height),
                pixels,
                ref info,
                DibRgbColors);
            if (copied != height)
            {
                throw new InvalidOperationException("A captura visual retornou dados incompletos.");
            }

            return new PixelFrame(width, height, stride, pixels);
        }
        finally
        {
            _ = NativeMethods.SelectObject(memoryDc, previous);
            _ = NativeMethods.DeleteObject(bitmap);
            _ = NativeMethods.DeleteDC(memoryDc);
            _ = NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr windowHandle);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr windowHandle, IntPtr deviceContext);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleBitmap(
            IntPtr deviceContext,
            int width,
            int height);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr graphicsObject);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr graphicsObject);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteDC(IntPtr deviceContext);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BitBlt(
            IntPtr destination,
            int destinationX,
            int destinationY,
            int width,
            int height,
            IntPtr source,
            int sourceX,
            int sourceY,
            uint operation);

        [DllImport("gdi32.dll")]
        public static extern int GetDIBits(
            IntPtr deviceContext,
            IntPtr bitmap,
            uint startScan,
            uint scanLines,
            [Out] byte[] bits,
            ref BitmapInfo info,
            uint usage);
    }
}
