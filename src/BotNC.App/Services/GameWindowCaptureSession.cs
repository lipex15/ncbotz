using System.IO;
using System.Runtime.InteropServices;
using BotNC.App.Models;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinRT;

namespace BotNC.App.Services;

/// <summary>
/// Captures one game window through Windows Graphics Capture. Unlike BitBlt/PrintWindow,
/// this continues to receive DirectX frames while another window is covering the game.
/// </summary>
public sealed class GameWindowCaptureSession : IAsyncDisposable
{
    private static readonly Guid GraphicsCaptureItemId =
        new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    private readonly IDirect3DDevice _device;
    private readonly GraphicsCaptureItem _item;
    private readonly Direct3D11CaptureFramePool _framePool;
    private readonly GraphicsCaptureSession _captureSession;
    private readonly SemaphoreSlim _frameAvailable = new(0, 1);
    private readonly object _frameSync = new();
    private bool _disposed;

    public GameWindowCaptureSession(GameWindowTarget target)
    {
        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new PlatformNotSupportedException(
                "A captura independente das janelas requer Windows Graphics Capture.");
        }

        _device = CreateDirect3DDevice();
        _item = CreateItemForWindow(target.Handle);
        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            _item.Size);
        _captureSession = _framePool.CreateCaptureSession(_item);
        _captureSession.IsCursorCaptureEnabled = false;
        // A captura é interna e não deve desenhar o contorno amarelo de privacidade
        // sobre a janela do jogo durante o uso normal do bot.
        TryDisableCaptureBorder(_captureSession);

        _framePool.FrameArrived += OnFrameArrived;
        _captureSession.StartCapture();
    }

    public async Task<PixelFrame> CaptureAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!await _frameAvailable.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken))
        {
            throw new TimeoutException("A janela do jogo não entregou um quadro visual em 3 segundos.");
        }

        Direct3D11CaptureFrame? latest = null;
        lock (_frameSync)
        {
            while (true)
            {
                var next = _framePool.TryGetNextFrame();
                if (next is null)
                {
                    break;
                }

                latest?.Dispose();
                latest = next;
            }
        }

        if (latest is null)
        {
            throw new InvalidOperationException("A captura sinalizou um quadro, mas não retornou a imagem.");
        }

        using (latest)
        using (var source = await SoftwareBitmap.CreateCopyFromSurfaceAsync(latest.Surface))
        using (var converted = SoftwareBitmap.Convert(
                   source,
                   BitmapPixelFormat.Bgra8,
                   BitmapAlphaMode.Premultiplied))
        {
            var stride = checked(converted.PixelWidth * 4);
            var pixels = new byte[checked(stride * converted.PixelHeight)];
            var buffer = new Windows.Storage.Streams.Buffer(checked((uint)pixels.Length));
            converted.CopyToBuffer(buffer);
            using var reader = DataReader.FromBuffer(buffer);
            reader.ReadBytes(pixels);
            return new PixelFrame(converted.PixelWidth, converted.PixelHeight, stride, pixels);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _framePool.FrameArrived -= OnFrameArrived;
        _captureSession.Dispose();
        _framePool.Dispose();
        _device.Dispose();
        _frameAvailable.Dispose();
        await Task.CompletedTask;
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object arguments)
    {
        if (_disposed || _frameAvailable.CurrentCount != 0)
        {
            return;
        }

        try
        {
            _frameAvailable.Release();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private static void TryDisableCaptureBorder(GraphicsCaptureSession session)
    {
        // O SDK usado pelo app compila contra o contrato 19041, que não expõe
        // IsBorderRequired no wrapper C#. O contrato mais novo está disponível
        // via IGraphicsCaptureSession3; quando o Windows permitir, desativamos
        // o indicador visual sem afetar a captura.
        try
        {
            var unknown = Marshal.GetIUnknownForObject(session);
            try
            {
                if (Marshal.GetObjectForIUnknown(unknown) is IGraphicsCaptureSession3 session3)
                {
                    session3.IsBorderRequired = false;
                }
            }
            finally
            {
                Marshal.Release(unknown);
            }
        }
        catch (COMException)
        {
            // Windows antigos ou sem consentimento mantêm o contorno; a captura
            // continua correta e o restante do bot não é interrompido.
        }
        catch (InvalidCastException)
        {
        }
    }

    [ComImport]
    [Guid("f2cdd966-22ae-5ea1-9596-3a289344c3be")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureSession3
    {
        bool IsBorderRequired { get; set; }
    }

    private static GraphicsCaptureItem CreateItemForWindow(IntPtr window)
    {
        using var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = factory.AsInterface<IGraphicsCaptureItemInterop>();
        var iid = GraphicsCaptureItemId;
        Marshal.ThrowExceptionForHR(interop.CreateForWindow(window, ref iid, out var pointer));
        try
        {
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    private static IDirect3DDevice CreateDirect3DDevice()
    {
        Marshal.ThrowExceptionForHR(NativeMethods.D3D11CreateDevice(
            IntPtr.Zero,
            NativeMethods.D3dDriverTypeHardware,
            IntPtr.Zero,
            NativeMethods.D3d11CreateDeviceBgraSupport,
            IntPtr.Zero,
            0,
            NativeMethods.D3d11SdkVersion,
            out var d3dDevice,
            out _,
            out var context));
        try
        {
            var iid = NativeMethods.IidIdxgiDevice;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3dDevice, in iid, out var dxgiDevice));
            try
            {
                Marshal.ThrowExceptionForHR(
                    NativeMethods.CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out var inspectable));
                try
                {
                    return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
                }
                finally
                {
                    Marshal.Release(inspectable);
                }
            }
            finally
            {
                Marshal.Release(dxgiDevice);
            }
        }
        finally
        {
            Marshal.Release(context);
            Marshal.Release(d3dDevice);
        }
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig]
        int CreateForWindow(IntPtr window, ref Guid iid, out IntPtr result);

        [PreserveSig]
        int CreateForMonitor(IntPtr monitor, ref Guid iid, out IntPtr result);
    }

    private static class NativeMethods
    {
        public const int D3dDriverTypeHardware = 1;
        public const uint D3d11CreateDeviceBgraSupport = 0x20;
        public const uint D3d11SdkVersion = 7;
        public static readonly Guid IidIdxgiDevice =
            new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");

        [DllImport("d3d11.dll")]
        public static extern int D3D11CreateDevice(
            IntPtr adapter,
            int driverType,
            IntPtr software,
            uint flags,
            IntPtr featureLevels,
            uint featureLevelCount,
            uint sdkVersion,
            out IntPtr device,
            out int featureLevel,
            out IntPtr immediateContext);

        [DllImport("d3d11.dll")]
        public static extern int CreateDirect3D11DeviceFromDXGIDevice(
            IntPtr dxgiDevice,
            out IntPtr graphicsDevice);
    }
}

public readonly record struct HpBarReading(bool Found, double Percent);

public static class HpBarAnalyzer
{
    public static HpBarReading Measure(PixelFrame frame)
    {
        // Night Crows keeps the HP bar in the lower-left corner in both normal and rest views.
        // Coordinates are relative to the captured game window (1936x1056 at the supported setup).
        var scaleX = frame.Width / 1936d;
        var scaleY = frame.Height / 1056d;
        var left = (int)Math.Round(101 * scaleX);
        var right = (int)Math.Round(303 * scaleX);
        var top = (int)Math.Round(983 * scaleY);
        var bottom = (int)Math.Round(994 * scaleY);
        if (left < 0 || right >= frame.Width || top < 0 || bottom >= frame.Height || right <= left)
        {
            return new HpBarReading(false, 0);
        }

        var lastFilled = -1;
        var filledColumns = 0;
        for (var x = left; x <= right; x++)
        {
            var redSamples = 0;
            for (var y = top; y <= bottom; y++)
            {
                var index = (y * frame.Stride) + (x * 4);
                var blue = frame.Pixels[index];
                var green = frame.Pixels[index + 1];
                var red = frame.Pixels[index + 2];
                if (red >= 95 && red >= green * 1.28 && red >= blue * 1.18)
                {
                    redSamples++;
                }
            }

            if (redSamples < 2)
            {
                continue;
            }

            filledColumns++;
            lastFilled = x;
        }

        if (lastFilled < left + 4 || filledColumns < 8)
        {
            return new HpBarReading(false, 0);
        }

        var percent = Math.Clamp((lastFilled - left + 1d) / (right - left + 1d), 0, 1);
        return new HpBarReading(true, percent);
    }
}
