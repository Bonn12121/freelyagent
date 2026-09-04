using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Freely.Computer;

public static class NativeInput
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;
    private const uint Unicode = 0x0004;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;
    private const uint MouseMiddleDown = 0x0020;
    private const uint MouseMiddleUp = 0x0040;
    private const uint MouseWheel = 0x0800;

    private static readonly IReadOnlyDictionary<string, ushort> Keys = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
    {
        ["backspace"] = 0x08, ["tab"] = 0x09, ["enter"] = 0x0D, ["shift"] = 0x10,
        ["ctrl"] = 0x11, ["control"] = 0x11, ["alt"] = 0x12, ["esc"] = 0x1B,
        ["escape"] = 0x1B, ["space"] = 0x20, ["pageup"] = 0x21, ["pagedown"] = 0x22,
        ["end"] = 0x23, ["home"] = 0x24, ["left"] = 0x25, ["up"] = 0x26,
        ["right"] = 0x27, ["down"] = 0x28, ["delete"] = 0x2E, ["win"] = 0x5B,
        ["f1"] = 0x70, ["f2"] = 0x71, ["f3"] = 0x72, ["f4"] = 0x73,
        ["f5"] = 0x74, ["f6"] = 0x75, ["f7"] = 0x76, ["f8"] = 0x77,
        ["f9"] = 0x78, ["f10"] = 0x79, ["f11"] = 0x7A, ["f12"] = 0x7B
    };

    public static void MovePointer(int x, int y)
    {
        if (!SetCursorPos(x, y)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public static async Task MovePointerHumanAsync(int x, int y, CancellationToken cancellationToken)
    {
        if (!GetCursorPos(out var start)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var distance = Math.Sqrt(Math.Pow(x - start.X, 2) + Math.Pow(y - start.Y, 2));
        var steps = Math.Clamp((int)(distance / 32), 5, 24);
        for (var step = 1; step <= steps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var progress = (double)step / steps;
            var eased = progress * progress * (3 - (2 * progress));
            MovePointer(
                (int)Math.Round(start.X + ((x - start.X) * eased)),
                (int)Math.Round(start.Y + ((y - start.Y) * eased)));
            await Task.Delay(8, cancellationToken).ConfigureAwait(false);
        }
    }

    public static void Click(string button, int clicks)
    {
        var (down, up) = button.ToLowerInvariant() switch
        {
            "right" => (MouseRightDown, MouseRightUp),
            "middle" => (MouseMiddleDown, MouseMiddleUp),
            _ => (MouseLeftDown, MouseLeftUp)
        };
        var inputs = Enumerable.Range(0, Math.Clamp(clicks, 1, 3))
            .SelectMany(_ => new[] { Mouse(down), Mouse(up) }).ToArray();
        Send(inputs);
    }

    public static void Scroll(int delta) => Send([Mouse(MouseWheel, unchecked((uint)delta))]);

    public static void TypeText(string text)
    {
        var inputs = new List<Input>(text.Length * 2);
        foreach (var character in text)
        {
            inputs.Add(Keyboard(0, character, Unicode));
            inputs.Add(Keyboard(0, character, Unicode | KeyUp));
        }
        Send(inputs.ToArray());
    }

    public static async Task TypeTextHumanAsync(string text, CancellationToken cancellationToken)
    {
        foreach (var character in text)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Send([Keyboard(0, character, Unicode), Keyboard(0, character, Unicode | KeyUp)]);
            await Task.Delay(6, cancellationToken).ConfigureAwait(false);
        }
    }

    public static void PressChord(string chord)
    {
        var names = chord.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (names.Length == 0) throw new ArgumentException("A key or key chord is required.", nameof(chord));
        var codes = names.Select(ResolveKey).ToArray();
        var inputs = codes.Select(code => Keyboard(code, '\0', 0))
            .Concat(codes.Reverse().Select(code => Keyboard(code, '\0', KeyUp))).ToArray();
        Send(inputs);
    }

    private static ushort ResolveKey(string name)
    {
        if (Keys.TryGetValue(name, out var code)) return code;
        if (name.Length == 1)
        {
            var scan = VkKeyScan(name[0]);
            if (scan != -1) return (ushort)(scan & 0xFF);
        }
        throw new ArgumentException($"Unsupported key '{name}'.");
    }

    private static Input Mouse(uint flags, uint data = 0) => new()
    {
        Type = InputMouse,
        Data = new InputUnion { Mouse = new MouseInput { Flags = flags, MouseData = data } }
    };

    private static Input Keyboard(ushort virtualKey, char scan, uint flags) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = virtualKey, Scan = scan, Flags = flags } }
    };

    private static void Send(Input[] inputs)
    {
        if (inputs.Length == 0) return;
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != (uint)inputs.Length) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows blocked simulated input. Elevated applications cannot be controlled by a non-elevated Freely process.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion Data; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern short VkKeyScan(char character);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
