using System.Runtime.InteropServices;

namespace BotNC.App.Services;

// Installed on the WPF dispatcher. Observes only event metadata, never typed text.
internal sealed class HumanInteractionMonitor : IDisposable
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<nint, byte> _windows;
    private readonly HookProc _mouseProc, _keyboardProc;
    private nint _mouse, _keyboard;
    private long _lastAt;
    private long _lastPhysicalAt = Environment.TickCount64;
    internal long LastPhysicalAt => Interlocked.Read(ref _lastPhysicalAt);
    internal long LastAt => Interlocked.Read(ref _lastAt);
    internal nint LastWindow { get; private set; }
    internal bool IsBusy => Environment.TickCount64 - Interlocked.Read(ref _lastAt) < 2500;
    private delegate nint HookProc(int code, nint message, nint data);
    internal HumanInteractionMonitor(IEnumerable<nint> windows)
    {
        _windows = new(windows.Distinct().Select(handle => new KeyValuePair<nint, byte>(handle, 0)));
        _lastAt = Environment.TickCount64 - 10000;
        _mouseProc = (code, message, data) => Observe(code, message, data, true);
        _keyboardProc = (code, message, data) => Observe(code, message, data, false);
        var module = GetModuleHandle(null);
        _mouse = SetWindowsHookEx(14, _mouseProc, module, 0);
        _keyboard = SetWindowsHookEx(13, _keyboardProc, module, 0);
        if (_mouse == 0 || _keyboard == 0)
        {
            Dispose();
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível observar a interação manual com o jogo.");
        }
    }
    internal void ReplaceWindow(nint previous, nint current)
    {
        _windows.TryRemove(previous, out _);
        _windows[current] = 0;
    }
    private nint Observe(int code, nint message, nint data, bool mouse)
    {
        if (code >= 0)
        {
            var flags = Marshal.ReadInt32(data, mouse ? 12 : 8);
            if (IsPhysicalEvent(mouse, flags))
            {
                Interlocked.Exchange(ref _lastPhysicalAt, Environment.TickCount64);
                if (_windows.ContainsKey(GetForegroundWindow()))
                {
                    LastWindow = GetForegroundWindow();
                    Interlocked.Exchange(ref _lastAt, Environment.TickCount64);
                }
            }
        }
        return CallNextHookEx(0, code, message, data);
    }
    public void Dispose()
    {
        if (_mouse != 0) UnhookWindowsHookEx(_mouse);
        if (_keyboard != 0) UnhookWindowsHookEx(_keyboard);
        _mouse = _keyboard = 0;
    }
    internal static bool IsPhysicalEvent(bool mouse, int flags) => (flags & (mouse ? 3 : 18)) == 0;
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? module);
}

internal sealed class HumanInteractionException() : Exception("Interação manual detectada; comando normal adiado sem refazer a rota.");
