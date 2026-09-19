using System.Runtime.InteropServices;
using System.Text;
using BotNC.App.Models;

namespace BotNC.App.Services;

public sealed class GameWindowService
{
    private const int ReferenceWindowWidth = 1920;
    private const int ReferenceWindowHeight = 1040;
    private static readonly HashSet<string> SupportedTitles =
        new(StringComparer.Ordinal)
        {
            "NIGHT CROWS(1)",
            "NIGHT CROWS(2)"
        };

    public IReadOnlyList<GameWindowTarget> Discover()
    {
        var windows = new List<GameWindowTarget>();
        NativeMethods.EnumWindows(
            (handle, _) =>
            {
                var title = ReadTitle(handle);
                if (!SupportedTitles.Contains(title))
                {
                    return true;
                }

                NativeMethods.GetWindowThreadProcessId(handle, out var processId);
                windows.Add(
                    new GameWindowTarget(
                        handle,
                        title,
                        checked((int)processId),
                        NativeMethods.IsIconic(handle),
                        NativeMethods.IsWindowVisible(handle)));
                return true;
            },
            IntPtr.Zero);

        return windows
            .OrderBy(window => window.Title, StringComparer.Ordinal)
            .ToArray();
    }

    public bool Activate(GameWindowTarget target)
    {
        if (!NativeMethods.IsWindow(target.Handle))
        {
            return false;
        }

        // Restaurar somente uma janela realmente minimizada. Chamar SW_RESTORE
        // em toda ativação desmaximiza o jogo e cria exatamente a janela pequena
        // no canto que não faz parte do fluxo do bot.
        var wasMinimized = NativeMethods.IsIconic(target.Handle);
        if (wasMinimized)
        {
            _ = NativeMethods.ShowWindow(target.Handle, NativeMethods.SwRestore);
        }

        // O contrato do bot é trabalhar com o cliente maximizado, mantendo a
        // moldura normal da janela. Isso corrige clientes que ficaram em modo
        // janela sem borda/maximizado diferente após uma troca ou encerramento.
        if (!NativeMethods.IsZoomed(target.Handle))
        {
            _ = NativeMethods.ShowWindow(target.Handle, NativeMethods.SwMaximize);
        }

        if (IsForeground(target) && NativeMethods.IsZoomed(target.Handle))
        {
            return true;
        }

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var foreground = NativeMethods.GetForegroundWindow();
            var foregroundThread = foreground == IntPtr.Zero
                ? 0
                : NativeMethods.GetWindowThreadProcessId(foreground, out _);
            var targetThread = NativeMethods.GetWindowThreadProcessId(target.Handle, out _);
            var currentThread = NativeMethods.GetCurrentThreadId();
            var attachedCurrent = currentThread != targetThread &&
                                  NativeMethods.AttachThreadInput(currentThread, targetThread, true);
            var attachedForeground = foregroundThread != 0 &&
                                     foregroundThread != targetThread &&
                                     foregroundThread != currentThread &&
                                     NativeMethods.AttachThreadInput(foregroundThread, targetThread, true);
            try
            {
                if (wasMinimized)
                {
                    _ = NativeMethods.ShowWindowAsync(target.Handle, NativeMethods.SwRestore);
                }
                if (!NativeMethods.IsZoomed(target.Handle))
                {
                    _ = NativeMethods.ShowWindowAsync(target.Handle, NativeMethods.SwMaximize);
                }
                _ = NativeMethods.BringWindowToTop(target.Handle);
                _ = NativeMethods.SetActiveWindow(target.Handle);
                _ = NativeMethods.SetFocus(target.Handle);
                _ = NativeMethods.SetForegroundWindow(target.Handle);
            }
            finally
            {
                if (attachedForeground)
                {
                    _ = NativeMethods.AttachThreadInput(foregroundThread, targetThread, false);
                }

                if (attachedCurrent)
                {
                    _ = NativeMethods.AttachThreadInput(currentThread, targetThread, false);
                }
            }

            if (IsForeground(target) && NativeMethods.IsZoomed(target.Handle))
            {
                return true;
            }

            Thread.Sleep(90);
        }

        return IsForeground(target);
    }

    public bool IsForeground(GameWindowTarget target)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
        return processId == target.ProcessId;
    }

    public (int X, int Y) MapReferencePoint(
        GameWindowTarget target,
        int referenceX,
        int referenceY)
    {
        if (!NativeMethods.IsWindow(target.Handle) ||
            !NativeMethods.GetWindowRect(target.Handle, out var rectangle))
        {
            throw new InvalidOperationException($"Não foi possível medir a janela {target.Title}.");
        }

        var width = rectangle.Right - rectangle.Left;
        var height = rectangle.Bottom - rectangle.Top;
        if (width < 640 || height < 360)
        {
            throw new InvalidOperationException(
                $"A janela {target.Title} está pequena demais para uma ação segura ({width}×{height}).");
        }

        var x = rectangle.Left + (int)Math.Round(
            Math.Clamp(referenceX, 0, ReferenceWindowWidth - 1) *
            width / (double)ReferenceWindowWidth);
        var y = rectangle.Top + (int)Math.Round(
            Math.Clamp(referenceY, 0, ReferenceWindowHeight - 1) *
            height / (double)ReferenceWindowHeight);
        return (x, y);
    }

    public (int Width, int Height) GetWindowSize(GameWindowTarget target)
    {
        if (!NativeMethods.IsWindow(target.Handle) ||
            !NativeMethods.GetWindowRect(target.Handle, out var rectangle))
        {
            return (0, 0);
        }

        return (rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);
    }

    private static string ReadTitle(IntPtr handle)
    {
        var length = NativeMethods.GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(length + 1);
        _ = NativeMethods.GetWindowText(handle, buffer, buffer.Capacity);
        return buffer.ToString().Trim();
    }

    private static class NativeMethods
    {
        public const int SwRestore = 9;
        public const int SwMaximize = 3;

        public delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr windowHandle, StringBuilder text, int maximumCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLength(IntPtr windowHandle);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsZoomed(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr windowHandle, out WindowRectangle rectangle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BringWindowToTop(IntPtr windowHandle);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr windowHandle, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindowAsync(IntPtr windowHandle, int command);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

        [DllImport("user32.dll")]
        public static extern IntPtr SetActiveWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        public static extern IntPtr SetFocus(IntPtr windowHandle);

        [StructLayout(LayoutKind.Sequential)]
        public struct WindowRectangle
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

    }
}
