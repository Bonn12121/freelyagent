using System.Runtime.InteropServices;

namespace Freely.Computer;

public sealed class EmergencyStopMonitor : IDisposable
{
    private const int KeyboardHook = 13;
    private const int LeftShift = 0xA0;
    private const int KeyDown = 0x0100;
    private const int KeyUp = 0x0101;
    private const uint Injected = 0x10;
    private readonly HookProcedure _procedure = HookCallback;
    private readonly ComputerControlCoordinator _coordinator;
    private nint _hook;
    private Timer? _holdTimer;
    private bool _leftShiftHeld;
    private static EmergencyStopMonitor? _active;

    public EmergencyStopMonitor(ComputerControlCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    public void Start()
    {
        if (_hook != nint.Zero) return;
        _active = this;
        _hook = SetWindowsHookEx(KeyboardHook, _procedure, GetModuleHandle(null), 0);
        if (_hook == nint.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Dispose()
    {
        _holdTimer?.Dispose();
        if (_hook != nint.Zero) UnhookWindowsHookEx(_hook);
        _hook = nint.Zero;
        if (ReferenceEquals(_active, this)) _active = null;
    }

    private static nint HookCallback(int code, nint message, nint data)
    {
        var monitor = _active;
        if (code >= 0 && monitor is not null)
        {
            var details = Marshal.PtrToStructure<KeyboardHookData>(data);
            if (details.VirtualKey == LeftShift && (details.Flags & Injected) == 0)
            {
                if ((int)message == KeyDown && !monitor._leftShiftHeld)
                {
                    monitor._leftShiftHeld = true;
                    monitor._holdTimer = new Timer(_ =>
                    {
                        monitor._holdTimer?.Dispose();
                        monitor._holdTimer = null;
                        monitor._coordinator.ForceStop();
                    }, null, 1000, Timeout.Infinite);
                }
                else if ((int)message == KeyUp)
                {
                    monitor._leftShiftHeld = false;
                    monitor._holdTimer?.Dispose();
                    monitor._holdTimer = null;
                }
            }
        }
        return CallNextHookEx(nint.Zero, code, message, data);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData
    {
        public int VirtualKey;
        public int ScanCode;
        public uint Flags;
        public int Time;
        public nuint ExtraInfo;
    }

    private delegate nint HookProcedure(int code, nint message, nint data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int hook, HookProcedure callback, nint module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}
