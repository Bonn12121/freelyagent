using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text.Json;
using Freely.Computer;
using Freely.Perception;
using Freely.Perception.Models;
using Freely.Tools.Browser;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace Freely.App.Browser;

public sealed class AppBrowserSession(ComputerControlCoordinator coordinator, PerceptionManager perception) : IBrowserSession
{
    private readonly Dictionary<string, BrowserElementTarget> _elements = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExternalElementTarget> _externalElements = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<BrowserChoice> _availableBrowsers = BrowserCatalog.Discover();
    private BrowserChoice _selectedBrowser = new("freely", "Freely Browser", null, true);
    private WebView2? _webView;
    private DispatcherQueue? _dispatcher;
    private nint _ownerWindow;
    private nint _externalWindow;

    public bool IsReady => _selectedBrowser.IsEmbedded ? _webView is not null && _ownerWindow != nint.Zero : _selectedBrowser.ExecutablePath is not null;
    public IReadOnlyList<BrowserChoice> AvailableBrowsers => _availableBrowsers;
    public string SelectedBrowserId => _selectedBrowser.Id;
    public string SelectedBrowserDisplayName => _selectedBrowser.DisplayName;
    public event EventHandler<bool>? VisibilityRequested;

    public void Attach(WebView2 webView, nint ownerWindow)
    {
        _webView = webView;
        _dispatcher = webView.DispatcherQueue;
        _ownerWindow = ownerWindow;
    }

    public void Detach(WebView2 webView)
    {
        if (!ReferenceEquals(_webView, webView)) return;
        _webView = null;
        _dispatcher = null;
        _ownerWindow = nint.Zero;
        _elements.Clear();
    }

    public void RefreshAvailableBrowsers()
    {
        _availableBrowsers = BrowserCatalog.Discover();
        SetSelectedBrowser(_selectedBrowser.Id);
    }

    public void SetSelectedBrowser(string? browserId)
    {
        _selectedBrowser = _availableBrowsers.FirstOrDefault(browser => string.Equals(browser.Id, browserId, StringComparison.OrdinalIgnoreCase))
            ?? _availableBrowsers.First(browser => browser.IsEmbedded);
        RequestVisibility(false);
    }

    public void RequestVisibility(bool visible) => VisibilityRequested?.Invoke(this, visible && _selectedBrowser.IsEmbedded);

    public Task<string> OpenAsync(Uri uri, CancellationToken cancellationToken)
    {
        RequestVisibility(true);
        if (!_selectedBrowser.IsEmbedded) return OpenExternalAsync(uri, cancellationToken);
        return RunOnUiThreadAsync(async webView =>
        {
            await EnsureReadyAsync(webView);
            var navigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs>? completed = null;
            completed = (_, args) => navigation.TrySetResult(args.IsSuccess);
            webView.CoreWebView2.NavigationCompleted += completed;
            try
            {
                webView.CoreWebView2.Navigate(uri.AbsoluteUri);
                if (!await navigation.Task.WaitAsync(TimeSpan.FromSeconds(45), cancellationToken))
                    throw new InvalidOperationException("The browser could not load that page.");
                await Task.Delay(250, cancellationToken);
                return await SnapshotCoreAsync(webView);
            }
            finally
            {
                webView.CoreWebView2.NavigationCompleted -= completed;
            }
        }, cancellationToken);
    }

    public Task<string> SnapshotAsync(CancellationToken cancellationToken)
    {
        RequestVisibility(true);
        if (!_selectedBrowser.IsEmbedded) return SnapshotExternalAsync(cancellationToken);
        return RunOnUiThreadAsync(async webView =>
        {
            await EnsureReadyAsync(webView);
            return await SnapshotCoreAsync(webView);
        }, cancellationToken);
    }

    public Task<string> ClickAsync(string elementId, CancellationToken cancellationToken)
    {
        RequestVisibility(true);
        if (!_selectedBrowser.IsEmbedded) return ClickExternalAsync(elementId, cancellationToken);
        return RunOnUiThreadAsync(async webView =>
        {
            await EnsureReadyAsync(webView);
            var target = RequireElement(elementId);
            var (x, y) = GetScreenCenter(webView, target);
            await coordinator.RunExclusiveAsync(async token =>
            {
                coordinator.FocusWindow(_ownerWindow);
                await Task.Delay(120, token);
                await NativeInput.MovePointerHumanAsync(x, y, token);
                NativeInput.Click("left", 1);
                return true;
            }, cancellationToken);
            return await SnapshotAfterInputAsync(webView, cancellationToken);
        }, cancellationToken);
    }

    public Task<string> TypeAsync(string elementId, string text, bool submit, CancellationToken cancellationToken)
    {
        RequestVisibility(true);
        if (!_selectedBrowser.IsEmbedded) return TypeExternalAsync(elementId, text, submit, cancellationToken);
        return RunOnUiThreadAsync(async webView =>
        {
            await EnsureReadyAsync(webView);
            var target = RequireElement(elementId);
            if (target.Type is not ("input" or "textarea" or "textbox" or "searchbox" or "combobox"))
                throw new InvalidOperationException($"Browser element '{elementId}' is not an editable field. Take a new snapshot.");
            var (x, y) = GetScreenCenter(webView, target);
            await coordinator.RunExclusiveAsync(async token =>
            {
                coordinator.FocusWindow(_ownerWindow);
                await Task.Delay(120, token);
                await NativeInput.MovePointerHumanAsync(x, y, token);
                NativeInput.Click("left", 1);
                await Task.Delay(100, token);
                NativeInput.PressChord("ctrl+a");
                await NativeInput.TypeTextHumanAsync(text, token);
                if (submit) NativeInput.PressChord("enter");
                return true;
            }, cancellationToken);
            return await SnapshotAfterInputAsync(webView, cancellationToken);
        }, cancellationToken);
    }

    public Task<string> ScrollAsync(int delta, CancellationToken cancellationToken)
    {
        RequestVisibility(true);
        if (!_selectedBrowser.IsEmbedded) return ScrollExternalAsync(delta, cancellationToken);
        return RunOnUiThreadAsync(async webView =>
        {
            await EnsureReadyAsync(webView);
            var (x, y) = GetScreenPoint(webView, webView.ActualWidth / 2, webView.ActualHeight / 2);
            await coordinator.RunExclusiveAsync(async token =>
            {
                coordinator.FocusWindow(_ownerWindow);
                await Task.Delay(120, token);
                await NativeInput.MovePointerHumanAsync(x, y, token);
                NativeInput.Scroll(Math.Clamp(delta, -2400, 2400));
                return true;
            }, cancellationToken);
            return await SnapshotAfterInputAsync(webView, cancellationToken);
        }, cancellationToken);
    }

    public Task<string> PressAsync(string keys, CancellationToken cancellationToken)
    {
        RequestVisibility(true);
        if (!_selectedBrowser.IsEmbedded) return PressExternalAsync(keys, cancellationToken);
        return RunOnUiThreadAsync(async webView =>
        {
            await EnsureReadyAsync(webView);
            webView.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await coordinator.RunExclusiveAsync(async token =>
            {
                coordinator.FocusWindow(_ownerWindow);
                await Task.Delay(120, token);
                NativeInput.PressChord(keys);
                return true;
            }, cancellationToken);
            return await SnapshotAfterInputAsync(webView, cancellationToken);
        }, cancellationToken);
    }

    private async Task<string> OpenExternalAsync(Uri uri, CancellationToken cancellationToken)
    {
        var executable = _selectedBrowser.ExecutablePath ?? throw new InvalidOperationException("The selected browser is no longer installed.");
        var startInfo = new ProcessStartInfo(executable) { UseShellExecute = false };
        startInfo.ArgumentList.Add(uri.AbsoluteUri);
        var launched = Process.Start(startInfo);
        var processName = Path.GetFileNameWithoutExtension(executable);
        for (var attempt = 0; attempt < 30; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(180, cancellationToken);
            launched?.Refresh();
            var window = launched is { HasExited: false, MainWindowHandle: not 0 }
                ? launched.MainWindowHandle
                : Process.GetProcessesByName(processName)
                    .Where(process => process.MainWindowHandle != nint.Zero)
                    .OrderByDescending(process => process.StartTime)
                    .Select(process => process.MainWindowHandle)
                    .FirstOrDefault();
            if (window == nint.Zero) continue;
            _externalWindow = window;
            coordinator.SetTargetWindow(window, ControlAccessScope.Browser);
            coordinator.FocusWindow(window);
            return await SnapshotExternalAsync(cancellationToken);
        }
        throw new InvalidOperationException($"{_selectedBrowser.DisplayName} opened, but Windows did not expose a controllable browser window.");
    }

    private async Task<string> SnapshotExternalAsync(CancellationToken cancellationToken)
    {
        if (_externalWindow == nint.Zero) throw new InvalidOperationException($"Open a page in {_selectedBrowser.DisplayName} before reading or controlling it.");
        coordinator.SetTargetWindow(_externalWindow, ControlAccessScope.Browser);
        coordinator.FocusWindow(_externalWindow);
        await Task.Delay(180, cancellationToken);
        var observation = await perception.ObserveAsync(
            new PerceptionTarget(PerceptionTargetKind.ActiveWindow),
            new ObservationOptions(ObservationDetail.Normal, 300, 50_000),
            cancellationToken);
        _externalElements.Clear();
        var externalObservations = new List<object>();
        foreach (var element in observation.Elements)
        {
            if (element.Bounds is { } bounds)
            {
                var id = $"browser_{externalObservations.Count + 1}";
                _externalElements[id] = new ExternalElementTarget(id, element.Type, bounds.X, bounds.Y, bounds.Width, bounds.Height);
                externalObservations.Add(new
                {
                    id,
                    type = element.Type,
                    name = element.Name,
                    value = element.Value,
                    description = element.Description,
                    enabled = element.Enabled,
                    selected = element.Selected,
                    bounds = new { x = bounds.X, y = bounds.Y, width = bounds.Width, height = bounds.Height }
                });
            }
        }
        var serialized = JsonSerializer.Serialize(new
        {
            source = "external_browser_uia",
            browser = _selectedBrowser.DisplayName,
            observation.Title,
            text = observation.PlainText,
            observation.Metadata,
            elements = externalObservations
        });
        return $"<observation source=\"external_browser_uia\" browser=\"{_selectedBrowser.DisplayName}\" input=\"physical_mouse_keyboard\">\n{serialized}\n</observation>";
    }

    private async Task<string> ClickExternalAsync(string elementId, CancellationToken cancellationToken)
    {
        var target = RequireExternalElement(elementId);
        var (x, y) = GetExternalScreenCenter(target);
        await coordinator.RunExclusiveAsync(async token =>
        {
            coordinator.FocusWindow(_externalWindow);
            await Task.Delay(120, token);
            await NativeInput.MovePointerHumanAsync(x, y, token);
            NativeInput.Click("left", 1);
            return true;
        }, cancellationToken);
        await Task.Delay(240, cancellationToken);
        return await SnapshotExternalAsync(cancellationToken);
    }

    private async Task<string> TypeExternalAsync(string elementId, string text, bool submit, CancellationToken cancellationToken)
    {
        var target = RequireExternalElement(elementId);
        if (target.Type is not ("input" or "document" or "combobox" or "element"))
            throw new InvalidOperationException($"Browser element '{elementId}' is not an editable field. Take a new snapshot.");
        var (x, y) = GetExternalScreenCenter(target);
        await coordinator.RunExclusiveAsync(async token =>
        {
            coordinator.FocusWindow(_externalWindow);
            await Task.Delay(120, token);
            await NativeInput.MovePointerHumanAsync(x, y, token);
            NativeInput.Click("left", 1);
            await Task.Delay(100, token);
            NativeInput.PressChord("ctrl+a");
            await NativeInput.TypeTextHumanAsync(text, token);
            if (submit) NativeInput.PressChord("enter");
            return true;
        }, cancellationToken);
        await Task.Delay(240, cancellationToken);
        return await SnapshotExternalAsync(cancellationToken);
    }

    private async Task<string> ScrollExternalAsync(int delta, CancellationToken cancellationToken)
    {
        var (x, y) = GetExternalWindowCenter();
        await coordinator.RunExclusiveAsync(async token =>
        {
            coordinator.FocusWindow(_externalWindow);
            await Task.Delay(120, token);
            await NativeInput.MovePointerHumanAsync(x, y, token);
            NativeInput.Scroll(Math.Clamp(delta, -2400, 2400));
            return true;
        }, cancellationToken);
        await Task.Delay(200, cancellationToken);
        return await SnapshotExternalAsync(cancellationToken);
    }

    private async Task<string> PressExternalAsync(string keys, CancellationToken cancellationToken)
    {
        await coordinator.RunExclusiveAsync(async token =>
        {
            coordinator.FocusWindow(_externalWindow);
            await Task.Delay(120, token);
            NativeInput.PressChord(keys);
            return true;
        }, cancellationToken);
        await Task.Delay(180, cancellationToken);
        return await SnapshotExternalAsync(cancellationToken);
    }

    private ExternalElementTarget RequireExternalElement(string elementId) =>
        _externalElements.TryGetValue(elementId, out var target)
            ? target
            : throw new InvalidOperationException($"Browser element '{elementId}' is stale or unavailable. Take a new snapshot.");

    private (int X, int Y) GetExternalScreenCenter(ExternalElementTarget target)
    {
        var window = GetExternalWindowRectangle();
        var visibleLeft = Math.Clamp(target.X, window.Left, window.Right);
        var visibleTop = Math.Clamp(target.Y, window.Top, window.Bottom);
        var visibleRight = Math.Clamp(target.X + target.Width, window.Left, window.Right);
        var visibleBottom = Math.Clamp(target.Y + target.Height, window.Top, window.Bottom);
        if (visibleRight - visibleLeft < 2 || visibleBottom - visibleTop < 2)
            throw new InvalidOperationException($"Browser element '{target.Id}' is outside the visible browser window. Scroll and take a new snapshot.");
        return ((int)Math.Round(visibleLeft + ((visibleRight - visibleLeft) / 2)),
            (int)Math.Round(visibleTop + ((visibleBottom - visibleTop) / 2)));
    }

    private (int X, int Y) GetExternalWindowCenter()
    {
        var window = GetExternalWindowRectangle();
        return (window.Left + ((window.Right - window.Left) / 2), window.Top + ((window.Bottom - window.Top) / 2));
    }

    private NativeRectangle GetExternalWindowRectangle()
    {
        if (_externalWindow == nint.Zero || !GetWindowRect(_externalWindow, out var rectangle))
            throw new InvalidOperationException("The selected browser window is no longer available.");
        return rectangle;
    }

    private static async Task EnsureReadyAsync(WebView2 webView)
    {
        await webView.EnsureCoreWebView2Async();
        webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
    }

    private async Task<string> SnapshotAfterInputAsync(WebView2 webView, CancellationToken cancellationToken)
    {
        await Task.Delay(220, cancellationToken);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                return await SnapshotCoreAsync(webView);
            }
            catch (COMException) when (attempt < 3)
            {
                await Task.Delay(250, cancellationToken);
            }
            catch (InvalidOperationException) when (attempt < 3)
            {
                await Task.Delay(250, cancellationToken);
            }
        }
        throw new InvalidOperationException("The page changed, but its updated state was not ready to read.");
    }

    private async Task<string> SnapshotCoreAsync(WebView2 webView)
    {
        var domSnapshotTask = webView.CoreWebView2.CallDevToolsProtocolMethodAsync(
            "DOMSnapshot.captureSnapshot",
            "{\"computedStyles\":[],\"includeDOMRects\":true,\"includePaintOrder\":false}").AsTask();
        var accessibilityTask = webView.CoreWebView2.CallDevToolsProtocolMethodAsync(
            "Accessibility.getFullAXTree",
            "{}").AsTask();
        await Task.WhenAll(domSnapshotTask, accessibilityTask);
        var domSnapshotJson = await domSnapshotTask;
        var accessibilityJson = await accessibilityTask;
        using var domSnapshot = JsonDocument.Parse(domSnapshotJson);
        using var accessibility = JsonDocument.Parse(accessibilityJson);
        var strings = domSnapshot.RootElement.GetProperty("strings").EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty).ToArray();
        var pageDocument = domSnapshot.RootElement.GetProperty("documents")[0];
        var nodes = pageDocument.GetProperty("nodes");
        var backendNodeIds = nodes.GetProperty("backendNodeId").EnumerateArray().Select(value => value.GetInt64()).ToArray();
        var nodeNameIndexes = nodes.GetProperty("nodeName").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        var accessibilityByBackendId = ReadAccessibilityNodes(accessibility.RootElement);
        var layout = pageDocument.GetProperty("layout");
        var layoutNodeIndexes = layout.GetProperty("nodeIndex").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        var layoutBounds = layout.GetProperty("bounds").EnumerateArray().ToArray();

        _elements.Clear();
        var observations = new List<object>();
        for (var layoutIndex = 0; layoutIndex < layoutNodeIndexes.Length && observations.Count < 220; layoutIndex++)
        {
            var nodeIndex = layoutNodeIndexes[layoutIndex];
            if (nodeIndex < 0 || nodeIndex >= backendNodeIds.Length || nodeIndex >= nodeNameIndexes.Length) continue;
            var bounds = layoutBounds[layoutIndex].EnumerateArray().Select(value => value.GetDouble()).ToArray();
            if (bounds.Length < 4 || bounds[2] <= 1 || bounds[3] <= 1 || bounds[0] + bounds[2] <= 0 || bounds[1] + bounds[3] <= 0 ||
                bounds[0] >= webView.ActualWidth || bounds[1] >= webView.ActualHeight) continue;
            var tag = strings[nodeNameIndexes[nodeIndex]].ToLowerInvariant();
            accessibilityByBackendId.TryGetValue(backendNodeIds[nodeIndex], out var axNode);
            var type = (axNode?.Role ?? tag).ToLowerInvariant();
            if (!IsUsefulElement(tag, type)) continue;

            var id = $"web_{backendNodeIds[nodeIndex]}";
            var target = new BrowserElementTarget(id, type, bounds[0], bounds[1], bounds[2], bounds[3]);
            _elements[id] = target;
            observations.Add(new
            {
                id,
                type,
                name = (axNode?.Name ?? string.Empty)[..Math.Min(axNode?.Name.Length ?? 0, 500)],
                value = axNode?.Protected == true ? "<PROTECTED>" : (axNode?.Value ?? string.Empty)[..Math.Min(axNode?.Value.Length ?? 0, 1000)],
                enabled = axNode?.Disabled != true,
                selected = axNode?.Selected == true,
                bounds = new { x = bounds[0], y = bounds[1], width = bounds[2], height = bounds[3] }
            });
        }

        var accessibleNames = accessibilityByBackendId.Values
            .Select(node => node.Name.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Take(1200)
            .ToList();
        var visualNames = observations.Count >= 24 && accessibleNames.Count >= 40
            ? []
            : await ReadVisualBrowserElementsAsync(webView, observations, accessibleNames);
        accessibleNames.AddRange(visualNames);
        var text = string.Join("\n", accessibleNames.Distinct(StringComparer.OrdinalIgnoreCase));
        if (text.Length > 30_000) text = text[..30_000];
        var observation = JsonSerializer.Serialize(new
        {
            source = visualNames.Count > 0 ? "webview_hybrid" : "webview_accessibility",
            inputMode = "physical_mouse_keyboard",
            type = "web_page",
            title = webView.CoreWebView2.DocumentTitle ?? string.Empty,
            url = webView.Source?.AbsoluteUri ?? string.Empty,
            text,
            elements = observations
        });
        return $"<observation source=\"webview_hybrid\" input=\"physical_mouse_keyboard\">\n{observation}\n</observation>";
    }

    private async Task<IReadOnlyList<string>> ReadVisualBrowserElementsAsync(
        WebView2 webView,
        List<object> observations,
        IReadOnlyCollection<string> accessibleNames)
    {
        try
        {
            coordinator.FocusWindow(_ownerWindow);
            await Task.Delay(100);
            var visual = await perception.ObserveAsync(
                new PerceptionTarget(PerceptionTargetKind.ActiveWindow),
                new ObservationOptions(ObservationDetail.Compact, 160, 18_000));
            var origin = GetScreenPoint(webView, 0, 0);
            var corner = GetScreenPoint(webView, webView.ActualWidth, webView.ActualHeight);
            var scale = webView.XamlRoot?.RasterizationScale ?? 1d;
            var names = new List<string>();
            foreach (var element in visual.Elements)
            {
                if (element.Bounds is not { } bounds || string.IsNullOrWhiteSpace(element.Name)) continue;
                var left = Math.Max(bounds.X, origin.X);
                var top = Math.Max(bounds.Y, origin.Y);
                var right = Math.Min(bounds.X + bounds.Width, corner.X);
                var bottom = Math.Min(bounds.Y + bounds.Height, corner.Y);
                if (right - left < 2 || bottom - top < 2) continue;
                if (accessibleNames.Any(name => string.Equals(name.Trim(), element.Name.Trim(), StringComparison.OrdinalIgnoreCase))) continue;

                var id = $"visual_web_{observations.Count + 1}";
                var x = (left - origin.X) / scale;
                var y = (top - origin.Y) / scale;
                var width = (right - left) / scale;
                var height = (bottom - top) / scale;
                _elements[id] = new BrowserElementTarget(id, "text", x, y, width, height);
                observations.Add(new
                {
                    id,
                    type = "text",
                    name = element.Name,
                    value = string.Empty,
                    enabled = true,
                    selected = false,
                    confidence = "visual_ocr",
                    bounds = new { x, y, width, height }
                });
                names.Add(element.Name);
            }
            return names;
        }
        catch (Exception exception) when (exception is PerceptionUnavailableException or COMException or InvalidOperationException)
        {
            return [];
        }
    }

    private static Dictionary<long, AccessibilityNode> ReadAccessibilityNodes(JsonElement root)
    {
        var result = new Dictionary<long, AccessibilityNode>();
        if (!root.TryGetProperty("nodes", out var nodes)) return result;
        foreach (var node in nodes.EnumerateArray())
        {
            if (!node.TryGetProperty("backendDOMNodeId", out var backendId) ||
                (node.TryGetProperty("ignored", out var ignored) && ignored.GetBoolean())) continue;
            var role = ReadAxText(node, "role");
            var name = ReadAxText(node, "name");
            var value = ReadAxText(node, "value");
            var disabled = false;
            var selected = false;
            var protectedValue = false;
            if (node.TryGetProperty("properties", out var properties))
            {
                foreach (var property in properties.EnumerateArray())
                {
                    var propertyName = property.GetProperty("name").GetString();
                    var propertyValue = ReadAxText(property, "value");
                    var enabled = propertyValue.Equals("true", StringComparison.OrdinalIgnoreCase);
                    if (propertyName == "disabled") disabled = enabled;
                    else if (propertyName is "selected" or "checked") selected |= enabled || propertyValue is "mixed";
                    else if (propertyName == "protected") protectedValue = enabled;
                }
            }
            result[backendId.GetInt64()] = new AccessibilityNode(role, name, value, disabled, selected, protectedValue);
        }
        return result;
    }

    private static string ReadAxText(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var property) || !property.TryGetProperty("value", out var value)) return string.Empty;
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
    }

    private static bool IsUsefulElement(string tag, string role) =>
        tag is "a" or "button" or "input" or "textarea" or "select" or "h1" or "h2" or "h3" ||
        role is "link" or "button" or "textbox" or "searchbox" or "combobox" or "checkbox" or "radio" or
            "switch" or "tab" or "menuitem" or "heading";

    private BrowserElementTarget RequireElement(string elementId) =>
        _elements.TryGetValue(elementId, out var target)
            ? target
            : throw new InvalidOperationException($"Browser element '{elementId}' is stale or unavailable. Take a new snapshot.");

    private (int X, int Y) GetScreenCenter(WebView2 webView, BrowserElementTarget target)
    {
        var visibleLeft = Math.Clamp(target.X, 0, webView.ActualWidth);
        var visibleTop = Math.Clamp(target.Y, 0, webView.ActualHeight);
        var visibleRight = Math.Clamp(target.X + target.Width, 0, webView.ActualWidth);
        var visibleBottom = Math.Clamp(target.Y + target.Height, 0, webView.ActualHeight);
        if (visibleRight - visibleLeft < 2 || visibleBottom - visibleTop < 2)
            throw new InvalidOperationException($"Browser element '{target.Id}' is outside the visible page. Scroll and take a new snapshot.");
        var centerX = visibleLeft + ((visibleRight - visibleLeft) / 2);
        var centerY = visibleTop + ((visibleBottom - visibleTop) / 2);
        return GetScreenPoint(webView, centerX, centerY);
    }

    private (int X, int Y) GetScreenPoint(WebView2 webView, double x, double y)
    {
        var webViewOrigin = webView.TransformToVisual(null).TransformPoint(new Point(0, 0));
        var scale = webView.XamlRoot?.RasterizationScale ?? 1d;
        var point = new NativePoint
        {
            X = (int)Math.Round((webViewOrigin.X + x) * scale),
            Y = (int)Math.Round((webViewOrigin.Y + y) * scale)
        };
        if (!ClientToScreen(_ownerWindow, ref point))
            throw new COMException("Windows could not resolve the browser element's screen position.", Marshal.GetLastWin32Error());
        return (point.X, point.Y);
    }

    private Task<T> RunOnUiThreadAsync<T>(Func<WebView2, Task<T>> action, CancellationToken cancellationToken)
    {
        var webView = _webView ?? throw new InvalidOperationException("Open the Chat page before using browser tools.");
        var dispatcher = _dispatcher ?? throw new InvalidOperationException("The browser UI is unavailable.");
        if (dispatcher.HasThreadAccess) return action(webView);

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(async () =>
        {
            try { completion.TrySetResult(await action(webView)); }
            catch (OperationCanceledException exception) { completion.TrySetCanceled(exception.CancellationToken); }
            catch (Exception exception) { completion.TrySetException(exception); }
        }))
        {
            completion.TrySetException(new InvalidOperationException("The browser UI is closing."));
        }
        return completion.Task.WaitAsync(cancellationToken);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ClientToScreen(nint window, ref NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint window, out NativeRectangle rectangle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private sealed record BrowserElementTarget(string Id, string Type, double X, double Y, double Width, double Height);
    private sealed record AccessibilityNode(string Role, string Name, string Value, bool Disabled, bool Selected, bool Protected);
    private sealed record ExternalElementTarget(string Id, string Type, double X, double Y, double Width, double Height);
}
