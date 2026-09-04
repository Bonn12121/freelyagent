using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using Freely.Perception.Models;

namespace Freely.Perception.Providers;

/// <summary>
/// Visual fallback for apps that expose an incomplete accessibility tree. The screenshot never leaves
/// the computer: Windows OCR converts visible text and geometry into the same model-neutral observation
/// format used by UI Automation.
/// </summary>
public sealed class ScreenOcrPerceptionProvider : IPerceptionProvider
{
    public int Priority => 50;
    public bool CanHandle(PerceptionTarget target) => target.Kind == PerceptionTargetKind.ActiveWindow;

    public async Task<Observation> ObserveAsync(
        PerceptionTarget target,
        ObservationOptions options,
        CancellationToken cancellationToken)
    {
        var handle = GetForegroundWindow();
        if (handle == nint.Zero || !GetWindowRect(handle, out var window) || window.Width < 2 || window.Height < 2)
        {
            throw new PerceptionUnavailableException("Windows could not capture the active window.");
        }

        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null) throw new PerceptionUnavailableException("No Windows OCR language is installed.");

        var maxDimension = Math.Max(1, OcrEngine.MaxImageDimension);
        var scale = Math.Min(1d, maxDimension / (double)Math.Max(window.Width, window.Height));
        var captureWidth = Math.Max(1, (int)Math.Round(window.Width * scale));
        var captureHeight = Math.Max(1, (int)Math.Round(window.Height * scale));

        using var fullSizeBitmap = new Bitmap(window.Width, window.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(fullSizeBitmap))
        {
            graphics.CopyFromScreen(window.Left, window.Top, 0, 0, new Size(window.Width, window.Height), CopyPixelOperation.SourceCopy);
        }

        using var scaledBitmap = scale < 1d
            ? new Bitmap(fullSizeBitmap, new Size(captureWidth, captureHeight))
            : null;
        var bitmap = scaledBitmap ?? fullSizeBitmap;

        await using var encoded = new MemoryStream();
        bitmap.Save(encoded, ImageFormat.Png);
        using var randomAccess = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(randomAccess.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(encoded.ToArray());
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }
        randomAccess.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(randomAccess);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        cancellationToken.ThrowIfCancellationRequested();
        var result = await engine.RecognizeAsync(softwareBitmap);
        cancellationToken.ThrowIfCancellationRequested();

        var elements = new List<SemanticElement>();
        var lines = new List<string>();
        foreach (var line in result.Lines)
        {
            if (elements.Count >= options.MaxElements) break;
            var text = line.Text.Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;
            var words = line.Words;
            if (words.Count == 0) continue;
            var left = words.Min(word => word.BoundingRect.X);
            var top = words.Min(word => word.BoundingRect.Y);
            var right = words.Max(word => word.BoundingRect.X + word.BoundingRect.Width);
            var bottom = words.Max(word => word.BoundingRect.Y + word.BoundingRect.Height);
            var bounds = new Bounds(
                window.Left + (left / scale),
                window.Top + (top / scale),
                (right - left) / scale,
                (bottom - top) / scale);
            var id = $"visual_text_{elements.Count + 1}";
            elements.Add(new SemanticElement(id, "text", text, null, "Visible text found by local OCR", true,
                null, null, bounds, ObservationConfidence.Medium));
            lines.Add($"[text:{id}] {text}");
        }

        var title = GetWindowTitle(handle);
        var metadata = new Dictionary<string, string>
        {
            ["capture"] = "active_window",
            ["processing"] = "local_windows_ocr",
            ["imageSharedWithModel"] = "false",
            ["elementCount"] = elements.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["windowBounds"] = string.Join(',', window.Left, window.Top, window.Width, window.Height)
        };
        return new Observation("screen_ocr", "application_visual", title, string.Join('\n', lines), elements,
            metadata, DateTimeOffset.UtcNow, elements.Count > 0);
    }

    private static string GetWindowTitle(nint handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0) return "Active window";
        var title = new char[length + 1];
        var copied = GetWindowText(handle, title, title.Length);
        return copied <= 0 ? "Active window" : new string(title, 0, copied);
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint window, out NativeRectangle rectangle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, char[] text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint window);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }
}
