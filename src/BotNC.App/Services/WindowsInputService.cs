using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BotNC.App.Services;

public sealed class WindowsInputService
{
    private static readonly TimeSpan CommandCooldown = TimeSpan.FromMilliseconds(1800);
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseMove = 0x0001;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseAbsolute = 0x8000;
    private const uint KeyboardScanCode = 0x0008;
    private const uint KeyboardKeyUp = 0x0002;
    private const uint MapVkToVsc = 0;
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;

    public async Task PressKeyAsync(
        int virtualKey,
        TimeSpan? hold = null,
        CancellationToken cancellationToken = default)
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

        await Task.Delay(CommandCooldown, cancellationToken);
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
        CancellationToken cancellationToken)
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
        await Task.Delay(CommandCooldown, cancellationToken);
    }

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
    }
}
