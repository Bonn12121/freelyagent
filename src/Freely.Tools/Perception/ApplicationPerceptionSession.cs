using Freely.Computer;
using Freely.Perception;
using Freely.Perception.Models;

namespace Freely.Tools.Perception;

public sealed record ApplicationElementTarget(
    string Id,
    string Type,
    string Name,
    string? Description,
    Bounds Bounds);

/// <summary>
/// Retains the latest native-app semantic map and refreshes it before element-based input, avoiding
/// coordinate guesses against stale or visually similar controls.
/// </summary>
public sealed class ApplicationPerceptionSession(
    PerceptionManager perception,
    ComputerControlCoordinator coordinator)
{
    private readonly object _sync = new();
    private readonly Dictionary<string, ApplicationElementTarget> _elements = new(StringComparer.OrdinalIgnoreCase);
    private Observation? _lastObservation;
    private DateTimeOffset _lastObservationAt;
    private nint _lastWindow;

    public async Task<Observation> ObserveAsync(
        PerceptionTarget target,
        ObservationOptions options,
        CancellationToken cancellationToken)
    {
        var observation = await perception.ObserveAsync(target, options, cancellationToken).ConfigureAwait(false);
        if (target.Kind == PerceptionTargetKind.ActiveWindow) Remember(observation, coordinator.TargetWindow);
        return observation;
    }

    public async Task<(ApplicationElementTarget Target, Observation Observation)> ResolveFreshAsync(
        string elementId,
        CancellationToken cancellationToken)
    {
        ApplicationElementTarget original;
        lock (_sync)
        {
            if (!_elements.TryGetValue(elementId, out original!))
            {
                throw new InvalidOperationException($"App element '{elementId}' is unknown. Call perception.read on the active window first.");
            }
            if (_lastObservation is not null && _lastWindow == coordinator.TargetWindow &&
                DateTimeOffset.UtcNow - _lastObservationAt < TimeSpan.FromSeconds(8))
            {
                return (original, _lastObservation);
            }
        }

        if (coordinator.TargetWindow == nint.Zero)
            throw new InvalidOperationException("No controlled app is selected. Focus or launch the app, then call perception.read.");
        coordinator.FocusTargetWindow();
        await Task.Delay(120, cancellationToken).ConfigureAwait(false);
        var observation = await perception.ObserveAsync(
            new PerceptionTarget(PerceptionTargetKind.ActiveWindow),
            new ObservationOptions(ObservationDetail.Compact, 180, 24_000),
            cancellationToken).ConfigureAwait(false);
        Remember(observation, coordinator.TargetWindow);

        ApplicationElementTarget? resolved;
        lock (_sync)
        {
            if (_elements.TryGetValue(elementId, out resolved)) return (resolved, observation);
            resolved = _elements.Values
                .Where(item => item.Type == original.Type &&
                    string.Equals(Normalize(item.Name), Normalize(original.Name), StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => Distance(item.Bounds, original.Bounds))
                .FirstOrDefault();
        }
        return resolved is not null
            ? (resolved, observation)
            : throw new InvalidOperationException($"App element '{elementId}' changed or disappeared. Read the app again before acting.");
    }

    public void Remember(Observation observation, nint window = default)
    {
        lock (_sync)
        {
            _elements.Clear();
            foreach (var element in observation.Elements)
            {
                if (element.Bounds is not { } bounds || string.IsNullOrWhiteSpace(element.Name)) continue;
                _elements[element.Id] = new ApplicationElementTarget(
                    element.Id, element.Type, element.Name, element.Description, bounds);
            }
            _lastObservation = observation;
            _lastObservationAt = DateTimeOffset.UtcNow;
            _lastWindow = window;
        }
    }

    private static string Normalize(string value) => string.Join(' ', value.Split((char[]?)null,
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static double Distance(Bounds first, Bounds second)
    {
        var firstX = first.X + (first.Width / 2);
        var firstY = first.Y + (first.Height / 2);
        var secondX = second.X + (second.Width / 2);
        var secondY = second.Y + (second.Height / 2);
        return Math.Sqrt(Math.Pow(firstX - secondX, 2) + Math.Pow(firstY - secondY, 2));
    }
}
