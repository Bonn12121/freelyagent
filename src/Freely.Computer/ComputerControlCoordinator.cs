namespace Freely.Computer;

using System.ComponentModel;
using System.Runtime.InteropServices;

public sealed class ComputerControlCoordinator : IDisposable
{
    private readonly SemaphoreSlim _desktopLock = new(1, 1);
    private readonly object _sync = new();
    private CancellationTokenSource _emergencyStop = new();
    private nint _targetWindow;
    private WindowsControlAccessIndicator? _accessIndicator;

    public event EventHandler? EmergencyStopped;

    public nint TargetWindow
    {
        get { lock (_sync) return _targetWindow; }
    }

    public void SetTargetWindow(nint window, ControlAccessScope scope = ControlAccessScope.Application)
    {
        if (window == nint.Zero || !IsWindow(window)) throw new ArgumentException("A valid target application window is required.", nameof(window));
        lock (_sync)
        {
            _targetWindow = window;
            ShowIndicator(scope, window);
        }
    }

    public void SetAccessScope(ControlAccessScope scope)
    {
        lock (_sync) ShowIndicator(scope, scope == ControlAccessScope.Computer ? nint.Zero : _targetWindow);
    }

    public void FocusTargetWindow()
    {
        nint window;
        lock (_sync) window = _targetWindow;
        if (window == nint.Zero || !IsWindow(window))
            throw new InvalidOperationException("No controlled app is selected. Launch or focus an app before using mouse or keyboard control.");
        FocusWindow(window);
    }

    public void FocusWindow(nint window)
    {
        if (window == nint.Zero || !IsWindow(window))
            throw new InvalidOperationException("The requested control window is no longer available.");
        if (!SetForegroundWindow(window))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows declined the request to focus the control window.");
    }

    public async Task<T> RunExclusiveAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        CancellationToken emergencyToken;
        lock (_sync) emergencyToken = _emergencyStop.Token;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, emergencyToken);
        await _desktopLock.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            return await action(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _desktopLock.Release();
        }
    }

    public void ForceStop()
    {
        CancellationTokenSource previous;
        lock (_sync)
        {
            previous = _emergencyStop;
            _emergencyStop = new CancellationTokenSource();
        }
        previous.Cancel();
        previous.Dispose();
        lock (_sync) _accessIndicator?.Hide();
        EmergencyStopped?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _accessIndicator?.Dispose();
            _accessIndicator = null;
        }
    }

    private void ShowIndicator(ControlAccessScope scope, nint window)
    {
        if (scope == ControlAccessScope.None)
        {
            _accessIndicator?.Hide();
            return;
        }
        _accessIndicator ??= new WindowsControlAccessIndicator();
        _accessIndicator.Show(scope, window);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint window);
}
