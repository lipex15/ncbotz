using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BotNC.App.Services;

public sealed record CapturedScreenPoint(int X, int Y);

public sealed class WindowsInputService
{
    private static readonly TimeSpan CommandCooldown = TimeSpan.FromMilliseconds(1800);
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseMove = 0x0001;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseAbsolute = 0x8000;
    private const uint MouseWheel = 0x0800;
    private const uint KeyboardScanCode = 0x0008;
    private const uint KeyboardKeyUp = 0x0002;
    private const uint MapVkToVsc = 0;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;

    public async Task PressKeyAsync(
        int virtualKey,
        TimeSpan? hold = null,
        CancellationToken cancellationToken = default,
        TimeSpan? cooldown = null)
    {
        KeyDown(virtualKey);
        try
        {
            await Task.Delay(hold ?? TimeSpan.FromMilliseconds(75), cancellationToken);
        }
        finally
        {
            KeyUp(virtualKey);
        }

        await Task.Delay(cooldown ?? CommandCooldown, cancellationToken);
    }

    public async Task PressEmergencyKeyAsync(
        int virtualKey,
        CancellationToken cancellationToken)
    {
        KeyDown(virtualKey);
        try
        {
            await Task.Delay(65, cancellationToken);
        }
        finally
        {
            KeyUp(virtualKey);
        }

        // Emergency repetitions must not inherit the normal 1.8 s workflow delay.
        await Task.Delay(650, cancellationToken);
    }

    public async Task PressKeyToWindowAsync(
        IntPtr window,
        int virtualKey,
        TimeSpan? hold = null,
        CancellationToken cancellationToken = default)
    {
        var scanCode = NativeMethods.MapVirtualKey(checked((uint)virtualKey), MapVkToVsc);
        var downLParam = new IntPtr(1 | ((int)scanCode << 16));
        var upLParam = new IntPtr(1 | ((int)scanCode << 16) | unchecked((int)0xC0000000));
        if (!NativeMethods.PostMessage(window, WmKeyDown, new IntPtr(virtualKey), downLParam))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "A janela do jogo não aceitou a tecla em segundo plano.");
        }

        await Task.Delay(hold ?? TimeSpan.FromMilliseconds(75), cancellationToken);
        if (!NativeMethods.PostMessage(window, WmKeyUp, new IntPtr(virtualKey), upLParam))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "A janela do jogo não aceitou a liberação da tecla em segundo plano.");
        }
    }

    public async Task HoldKeyAsync(
        int virtualKey,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        KeyDown(virtualKey);
        try
        {
            await Task.Delay(duration, cancellationToken);
        }
        finally
        {
            KeyUp(virtualKey);
        }

        await Task.Delay(CommandCooldown, cancellationToken);
    }

    public async Task ClickAsync(
        int screenX,
        int screenY,
        CancellationToken cancellationToken,
        TimeSpan? cooldown = null)
    {
        var width = NativeMethods.GetSystemMetrics(SmCxScreen);
        var height = NativeMethods.GetSystemMetrics(SmCyScreen);
        var normalizedX = (int)Math.Round(screenX * 65535d / Math.Max(1, width - 1));
        var normalizedY = (int)Math.Round(screenY * 65535d / Math.Max(1, height - 1));
        Send(CreateMouseInput(normalizedX, normalizedY, MouseMove | MouseAbsolute));
        await Task.Delay(80, cancellationToken);
        Send(CreateMouseInput(
            normalizedX,
            normalizedY,
            MouseMove | MouseAbsolute | MouseLeftDown));
        await Task.Delay(70, cancellationToken);
        Send(CreateMouseInput(
            normalizedX,
            normalizedY,
            MouseMove | MouseAbsolute | MouseLeftUp));
        await Task.Delay(cooldown ?? CommandCooldown, cancellationToken);
    }

    public async Task MoveAndClickAsync(
        int screenX,
        int screenY,
        TimeSpan movementDuration,
        CancellationToken cancellationToken,
        TimeSpan? cooldown = null)
    {
        var width = NativeMethods.GetSystemMetrics(SmCxScreen);
        var height = NativeMethods.GetSystemMetrics(SmCyScreen);
        if (!NativeMethods.GetCursorPos(out var current))
        {
            current = new CursorPoint { X = screenX, Y = screenY };
        }

        var steps = Math.Clamp((int)(movementDuration.TotalMilliseconds / 22), 8, 48);
        var delay = TimeSpan.FromMilliseconds(
            Math.Max(8, movementDuration.TotalMilliseconds / steps));
        for (var step = 1; step <= steps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var progress = step / (double)steps;
            // Curva suave: começa e termina devagar, evitando que o jogo perca
            // um salto instantâneo do ponteiro em máquinas mais lentas.
            var eased = progress * progress * (3 - (2 * progress));
            var x = (int)Math.Round(current.X + ((screenX - current.X) * eased));
            var y = (int)Math.Round(current.Y + ((screenY - current.Y) * eased));
            Send(CreateAbsoluteMouseMove(x, y, width, height));
            await Task.Delay(delay, cancellationToken);
        }

        var normalizedX = (int)Math.Round(screenX * 65535d / Math.Max(1, width - 1));
        var normalizedY = (int)Math.Round(screenY * 65535d / Math.Max(1, height - 1));
        await Task.Delay(140, cancellationToken);
        Send(CreateMouseInput(
            normalizedX,
            normalizedY,
            MouseMove | MouseAbsolute | MouseLeftDown));
        await Task.Delay(110, cancellationToken);
        Send(CreateMouseInput(
            normalizedX,
            normalizedY,
            MouseMove | MouseAbsolute | MouseLeftUp));
        await Task.Delay(cooldown ?? CommandCooldown, cancellationToken);
    }

    public async Task ScrollAsync(int wheelDelta, int repetitions, CancellationToken cancellationToken)
    {
        for (var index = 0; index < repetitions; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var wheel = CreateMouseInput(0, 0, MouseWheel);
            wheel.Union.Mouse.MouseData = unchecked((uint)wheelDelta);
            Send(wheel);
            await Task.Delay(180, cancellationToken);
        }

        await Task.Delay(CommandCooldown, cancellationToken);
    }

    public static async Task<CapturedScreenPoint> CaptureNextLeftClickAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && IsLeftButtonPressed())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(30, cancellationToken);
        }

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsLeftButtonPressed() && NativeMethods.GetCursorPos(out var point))
            {
                while (IsLeftButtonPressed())
                {
                    await Task.Delay(20, cancellationToken);
                }

                return new CapturedScreenPoint(point.X, point.Y);
            }

            await Task.Delay(20, cancellationToken);
        }

        throw new TimeoutException("Nenhum clique foi capturado em 60 segundos.");
    }

    private static bool IsLeftButtonPressed() =>
        (NativeMethods.GetAsyncKeyState(0x01) & 0x8000) != 0;

    private static void KeyDown(int virtualKey) =>
        Send(CreateKeyboardInput(virtualKey, keyUp: false));

    private static void KeyUp(int virtualKey) =>
        Send(CreateKeyboardInput(virtualKey, keyUp: true));

    private static NativeInput CreateKeyboardInput(int virtualKey, bool keyUp)
    {
        var scanCode = NativeMethods.MapVirtualKey(checked((uint)virtualKey), MapVkToVsc);
        return new NativeInput
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    ScanCode = checked((ushort)scanCode),
                    Flags = KeyboardScanCode | (keyUp ? KeyboardKeyUp : 0)
                }
            }
        };
    }

    private static NativeInput CreateMouseInput(int x, int y, uint flags) =>
        new()
        {
            Type = InputMouse,
            Union = new InputUnion
            {
                Mouse = new MouseInput
                {
                    Dx = x,
                    Dy = y,
                    Flags = flags
                }
            }
        };

    private static NativeInput CreateAbsoluteMouseMove(int x, int y, int width, int height) =>
        CreateMouseInput(
            (int)Math.Round(x * 65535d / Math.Max(1, width - 1)),
            (int)Math.Round(y * 65535d / Math.Max(1, height - 1)),
            MouseMove | MouseAbsolute);

    private static void Send(NativeInput input)
    {
        var inputs = new[] { input };
        var sent = NativeMethods.SendInput(1, inputs, Marshal.SizeOf<NativeInput>());
        if (sent != 1)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "O Windows não aceitou o comando de entrada.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(
            uint inputCount,
            [In] NativeInput[] inputs,
            int inputSize);

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint code, uint mapType);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out CursorPoint point);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    }
}
