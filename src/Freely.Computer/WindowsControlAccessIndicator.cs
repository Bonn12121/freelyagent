using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Freely.Computer;

public enum ControlAccessScope { None, Browser, Application, Computer }

/// <summary>A click-through, always-on-top boundary showing exactly what Freely may control.</summary>
internal sealed class WindowsControlAccessIndicator : IDisposable
{
    private const int UpdateMessage = 0x8001;
    private const int WmClose = 0x0010;
    private const int WmDestroy = 0x0002;
    private const int WmNcHitTest = 0x0084;
    private const int WmTimer = 0x0113;
    private const int HtTransparent = -1;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExLayered = 0x00080000;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const uint UlwAlpha = 0x00000002;
    private const byte AcSrcAlpha = 0x01;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    private static readonly ConcurrentDictionary<nint, WindowsControlAccessIndicator> Instances = new();
    private static readonly NativeWindowProcedure WindowProcedureDelegate = WindowProcedure;
    private static readonly object RegistrationSync = new();
    private static ushort _windowClass;

    private readonly object _sync = new();
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private nint _window;
    private ControlAccessScope _scope;
    private nint _targetWindow;
    private NativeRectangle _lastTarget;
    private ControlAccessScope _lastScope;
    private bool _disposed;

    public WindowsControlAccessIndicator()
    {
        _thread = new Thread(RunMessageLoop) { IsBackground = true, Name = "Freely control access indicator" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(2));
    }

    public void Show(ControlAccessScope scope, nint targetWindow)
    {
        if (scope == ControlAccessScope.None) { Hide(); return; }
        lock (_sync)
        {
            if (_disposed) return;
            _scope = scope;
            _targetWindow = targetWindow;
        }
        if (_window != nint.Zero) PostMessage(_window, UpdateMessage, nint.Zero, nint.Zero);
    }

    public void Hide()
    {
        lock (_sync)
        {
            _scope = ControlAccessScope.None;
            _targetWindow = nint.Zero;
        }
        if (_window != nint.Zero) PostMessage(_window, UpdateMessage, nint.Zero, nint.Zero);
    }

    public void Dispose()
    {
        lock (_sync) _disposed = true;
        if (_window != nint.Zero) PostMessage(_window, WmClose, nint.Zero, nint.Zero);
        if (_thread.IsAlive) _thread.Join(TimeSpan.FromSeconds(1));
        _ready.Dispose();
    }

    private void RunMessageLoop()
    {
        EnsureWindowClass();
        _window = CreateWindowEx(WsExLayered | WsExTransparent | WsExToolWindow | WsExNoActivate,
            "FreelyControlBoundary", string.Empty, WsPopup, 0, 0, 0, 0, nint.Zero, nint.Zero,
            GetModuleHandle(null), nint.Zero);
        if (_window != nint.Zero)
        {
            Instances[_window] = this;
            SetTimer(_window, 1, 250, nint.Zero);
        }
        _ready.Set();
        while (GetMessage(out var message, nint.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
        if (_window != nint.Zero) Instances.TryRemove(_window, out _);
        _window = nint.Zero;
    }

    private static void EnsureWindowClass()
    {
        lock (RegistrationSync)
        {
            if (_windowClass != 0) return;
            var windowClass = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(),
                WindowProcedure = WindowProcedureDelegate,
                Instance = GetModuleHandle(null),
                ClassName = "FreelyControlBoundary"
            };
            _windowClass = RegisterClassEx(ref windowClass);
        }
    }

    private static nint WindowProcedure(nint window, uint message, nint wParam, nint lParam)
    {
        if (message == WmNcHitTest) return new nint(HtTransparent);
        if (Instances.TryGetValue(window, out var indicator))
        {
            if (message is UpdateMessage or WmTimer)
            {
                indicator.UpdateWindow();
                return nint.Zero;
            }
            if (message == WmDestroy)
            {
                PostQuitMessage(0);
                return nint.Zero;
            }
        }
        return DefWindowProc(window, message, wParam, lParam);
    }

    private void UpdateWindow()
    {
        ControlAccessScope scope;
        nint targetWindow;
        lock (_sync)
        {
            scope = _scope;
            targetWindow = _targetWindow;
        }
        if (scope == ControlAccessScope.None)
        {
            ShowWindow(_window, 0);
            _lastScope = ControlAccessScope.None;
            return;
        }

        NativeRectangle target;
        if (scope == ControlAccessScope.Computer)
        {
            var left = GetSystemMetrics(SmXVirtualScreen);
            var top = GetSystemMetrics(SmYVirtualScreen);
            target = new NativeRectangle(left, top, left + GetSystemMetrics(SmCxVirtualScreen),
                top + GetSystemMetrics(SmCyVirtualScreen));
        }
        else if (targetWindow == nint.Zero || !IsWindow(targetWindow) || IsIconic(targetWindow) ||
                 !GetWindowRect(targetWindow, out target))
        {
            ShowWindow(_window, 0);
            return;
        }

        if (scope == _lastScope && target.Equals(_lastTarget) && IsWindowVisible(_window)) return;
        _lastScope = scope;
        _lastTarget = target;
        Draw(target);
    }

    private void Draw(NativeRectangle target)
    {
        const int padding = 32;
        var width = Math.Max(40, target.Width + (padding * 2));
        var height = Math.Max(40, target.Height + (padding * 2));
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var rectangle = new RectangleF(padding, padding, width - (padding * 2) - 1, height - (padding * 2) - 1);
            using var boundary = RoundedRectangle(rectangle, 11);
            DrawGlowPass(graphics, boundary, 54, Color.FromArgb(8, 10, 92, 255));
            DrawGlowPass(graphics, boundary, 42, Color.FromArgb(12, 8, 108, 255));
            DrawGlowPass(graphics, boundary, 32, Color.FromArgb(18, 0, 126, 255));
            DrawGlowPass(graphics, boundary, 24, Color.FromArgb(25, 0, 140, 255));
            DrawGlowPass(graphics, boundary, 16, Color.FromArgb(38, 0, 151, 255));
            DrawGlowPass(graphics, boundary, 10, Color.FromArgb(72, 0, 158, 255));
            DrawGlowPass(graphics, boundary, 6, Color.FromArgb(132, 0, 166, 255));
            DrawGlowPass(graphics, boundary, 3, Color.FromArgb(245, 35, 174, 255));
            DrawGlowPass(graphics, boundary, 1, Color.FromArgb(255, 116, 207, 255));
        }

        var screenDc = GetDC(nint.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
        var previous = SelectObject(memoryDc, bitmapHandle);
        try
        {
            var destination = new NativePoint(target.Left - padding, target.Top - padding);
            var size = new NativeSize(width, height);
            var source = new NativePoint(0, 0);
            var blend = new BlendFunction(0, 0, 255, AcSrcAlpha);
            UpdateLayeredWindow(_window, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, UlwAlpha);
        }
        finally
        {
            SelectObject(memoryDc, previous);
            DeleteObject(bitmapHandle);
            DeleteDC(memoryDc);
            ReleaseDC(nint.Zero, screenDc);
        }
        SetWindowPos(_window, new nint(-1), target.Left - padding, target.Top - padding, width, height, 0x0010 | 0x0040);
    }

    private static void DrawGlowPass(Graphics graphics, GraphicsPath path, float width, Color color)
    {
        using var pen = new Pen(color, width) { LineJoin = LineJoin.Round };
        graphics.DrawPath(pen, path);
    }

    private static GraphicsPath RoundedRectangle(RectangleF rectangle, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint NativeWindowProcedure(nint window, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public NativeWindowProcedure WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint(int x, int y) { public readonly int X = x; public readonly int Y = y; }
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeSize(int width, int height) { public readonly int Width = width; public readonly int Height = height; }
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRectangle(int left, int top, int right, int bottom) : IEquatable<NativeRectangle>
    {
        public readonly int Left = left;
        public readonly int Top = top;
        public readonly int Right = right;
        public readonly int Bottom = bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
        public bool Equals(NativeRectangle other) => Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;
        public override bool Equals(object? value) => value is NativeRectangle other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);
    }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct BlendFunction(byte blendOp, byte blendFlags, byte sourceAlpha, byte alphaFormat)
    {
        public readonly byte BlendOp = blendOp;
        public readonly byte BlendFlags = blendFlags;
        public readonly byte SourceConstantAlpha = sourceAlpha;
        public readonly byte AlphaFormat = alphaFormat;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Window;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public NativePoint Point;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(int exStyle, string className, string title, int style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern int GetMessage(out NativeMessage message, nint window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref NativeMessage message);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(ref NativeMessage message);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int exitCode);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, int message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nuint SetTimer(nint window, nuint id, uint milliseconds, nint callback);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeRectangle rectangle);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(nint window, nint destinationDc, ref NativePoint destination, ref NativeSize size, nint sourceDc, ref NativePoint source, uint colorKey, ref BlendFunction blend, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? moduleName);
}
