using System.IO;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinRT;

if (args.Length < 2)
{
    return 2;
}

var handle = (IntPtr)long.Parse(args[0]);
var output = Path.GetFullPath(args[1]);
using var device = CreateDevice();
var item = CreateItem(handle);
using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
    device,
    DirectXPixelFormat.B8G8R8A8UIntNormalized,
    2,
    item.Size);
using var session = pool.CreateCaptureSession(item);
var completion = new TaskCompletionSource<Direct3D11CaptureFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
pool.FrameArrived += (_, _) =>
{
    var frame = pool.TryGetNextFrame();
    if (!completion.TrySetResult(frame))
    {
        frame.Dispose();
    }
};
session.StartCapture();
using var captured = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(captured.Surface);
using var converted = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
var bytes = new byte[converted.PixelWidth * converted.PixelHeight * 4];
var buffer = new Windows.Storage.Streams.Buffer((uint)bytes.Length);
converted.CopyToBuffer(buffer);
using var reader = DataReader.FromBuffer(buffer);
reader.ReadBytes(bytes);
await File.WriteAllBytesAsync(output, EncodePng(bytes, converted.PixelWidth, converted.PixelHeight));
Console.WriteLine($"{converted.PixelWidth}x{converted.PixelHeight} -> {output}");
return 0;

static byte[] EncodePng(byte[] pixels, int width, int height)
{
    using var stream = new MemoryStream();
    var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
        width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, width * 4);
    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
    encoder.Save(stream);
    return stream.ToArray();
}

static GraphicsCaptureItem CreateItem(IntPtr window)
{
    using var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
    var interop = factory.AsInterface<IGraphicsCaptureItemInterop>();
    var iid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
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

static IDirect3DDevice CreateDevice()
{
    var result = NativeMethods.D3D11CreateDevice(
        IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7,
        out var d3dDevice, out _, out var context);
    Marshal.ThrowExceptionForHR(result);
    try
    {
        var iid = new Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3dDevice, in iid, out var dxgiDevice));
        try
        {
            Marshal.ThrowExceptionForHR(NativeMethods.CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out var inspectable));
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
interface IGraphicsCaptureItemInterop
{
    [PreserveSig]
    int CreateForWindow(IntPtr window, ref Guid iid, out IntPtr result);

    [PreserveSig]
    int CreateForMonitor(IntPtr monitor, ref Guid iid, out IntPtr result);
}

static class NativeMethods
{
    [DllImport("d3d11.dll")]
    public static extern int D3D11CreateDevice(
        IntPtr adapter, int driverType, IntPtr software, uint flags,
        IntPtr featureLevels, uint featureLevelCount, uint sdkVersion,
        out IntPtr device, out int featureLevel, out IntPtr immediateContext);

    [DllImport("d3d11.dll")]
    public static extern int CreateDirect3D11DeviceFromDXGIDevice(
        IntPtr dxgiDevice, out IntPtr graphicsDevice);
}
