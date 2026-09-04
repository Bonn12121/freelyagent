using Freely.Perception.Models;

namespace Freely.Perception;

public interface IPerceptionProvider
{
    int Priority { get; }
    bool CanHandle(PerceptionTarget target);
    Task<Observation> ObserveAsync(PerceptionTarget target, ObservationOptions options, CancellationToken cancellationToken);
}

public sealed class PerceptionManager(IEnumerable<IPerceptionProvider> providers)
{
    private readonly IReadOnlyList<IPerceptionProvider> _providers = providers.OrderByDescending(provider => provider.Priority).ToArray();

    public async Task<Observation> ObserveAsync(
        PerceptionTarget target,
        ObservationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedOptions = options ?? new ObservationOptions();
        var observations = new List<Observation>();
        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!provider.CanHandle(target)) continue;
            try
            {
                var observation = await provider.ObserveAsync(target, resolvedOptions, cancellationToken).ConfigureAwait(false);
                observations.Add(observation);
                if (target.Kind != PerceptionTargetKind.ActiveWindow && observation.IsSufficient) return observation;
                if (target.Kind == PerceptionTargetKind.ActiveWindow && ShouldUseStructuralFastPath(observation, resolvedOptions))
                    return observation;
            }
            catch (Exception exception) when (exception is PerceptionUnavailableException or InvalidOperationException or
                System.Runtime.InteropServices.ExternalException or UnauthorizedAccessException or System.IO.FileNotFoundException)
            {
                // A later provider may be able to observe the same target using a different source.
            }
        }

        if (observations.Count == 1) return observations[0];
        if (observations.Count > 1) return Fuse(observations, resolvedOptions);
        throw new PerceptionUnavailableException($"No perception provider could read {target.Kind}.");
    }

    /// <summary>
    /// A healthy accessibility tree is both faster and more precise than OCR. OCR remains the fallback for
    /// canvas-heavy, image-heavy, and custom-drawn interfaces, while Full detail always combines both views.
    /// </summary>
    private static bool ShouldUseStructuralFastPath(Observation observation, ObservationOptions options)
    {
        if (!observation.IsSufficient || observation.Source == "screen_ocr" || options.Detail == ObservationDetail.Full)
            return false;

        var named = observation.Elements.Count(element => !string.IsNullOrWhiteSpace(element.Name));
        var actionable = observation.Elements.Count(element => element.Type is
            "input" or "edit" or "textbox" or "document" or "combobox" or "button" or "link" or
            "checkbox" or "radio" or "tab" or "menu_item" or "list_item" or "tree_item");
        return options.Detail == ObservationDetail.Compact
            ? named >= 8 && actionable >= 3
            : named >= 12 && actionable >= 3;
    }

    private static Observation Fuse(IReadOnlyList<Observation> observations, ObservationOptions options)
    {
        var primary = observations[0];
        var elements = new List<SemanticElement>(Math.Min(options.MaxElements, observations.Sum(item => item.Elements.Count)));
        var passwordBounds = observations.SelectMany(item => item.Elements)
            .Where(item => item.Value == "<REDACTED_PASSWORD>" && item.Bounds is not null)
            .Select(item => item.Bounds!)
            .ToArray();

        var visualObservations = observations.Where(item => item.Source == "screen_ocr").ToArray();
        var structuralObservations = observations.Where(item => item.Source != "screen_ocr").ToArray();
        var visualBudget = visualObservations.Length == 0 ? 0 : Math.Max(32, options.MaxElements / 3);
        visualBudget = Math.Min(visualBudget, options.MaxElements);
        var structuralBudget = options.MaxElements - visualBudget;

        foreach (var observation in structuralObservations)
        {
            foreach (var candidate in observation.Elements.OrderByDescending(StructuralPriority).Take(structuralBudget))
            {
                if (elements.Any(existing => IsDuplicate(existing, candidate))) continue;
                elements.Add(candidate);
            }
        }

        var visualAdded = 0;
        foreach (var observation in visualObservations)
        {
            foreach (var candidate in observation.Elements)
            {
                if (visualAdded >= visualBudget || elements.Count >= options.MaxElements) break;
                if (candidate.Bounds is { } visualBounds && passwordBounds.Any(password => Intersects(password, visualBounds))) continue;
                if (elements.Any(existing => IsDuplicate(existing, candidate))) continue;
                elements.Add(candidate);
                visualAdded++;
            }
        }

        if (elements.Count < options.MaxElements)
        {
            foreach (var candidate in structuralObservations.SelectMany(item => item.Elements).OrderByDescending(StructuralPriority))
            {
                if (elements.Count >= options.MaxElements) break;
                if (elements.Any(existing => IsDuplicate(existing, candidate))) continue;
                elements.Add(candidate);
            }
        }

        var plainTextParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(primary.PlainText)) plainTextParts.Add(primary.PlainText);
        var visualText = elements.Where(item => item.Id.StartsWith("visual_text_", StringComparison.Ordinal))
            .Select(item => $"[visual:{item.Id}] {item.Name}")
            .ToArray();
        if (visualText.Length > 0) plainTextParts.Add("VISIBLE TEXT (local OCR fallback):\n" + string.Join('\n', visualText));
        var plainText = string.Join("\n\n", plainTextParts);
        if (plainText.Length > options.MaxTextCharacters) plainText = plainText[..options.MaxTextCharacters];

        var metadata = new Dictionary<string, string>(primary.Metadata)
        {
            ["sources"] = string.Join(',', observations.Select(item => item.Source)),
            ["fusion"] = "accessibility_plus_local_visual_ocr",
            ["imageSharedWithModel"] = "false",
            ["elementCount"] = elements.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        return new Observation("fused_perception", primary.Type, primary.Title, plainText, elements, metadata,
            DateTimeOffset.UtcNow, observations.Any(item => item.IsSufficient));
    }

    private static bool IsDuplicate(SemanticElement existing, SemanticElement candidate)
    {
        if (candidate.Id == existing.Id) return true;
        if (string.IsNullOrWhiteSpace(candidate.Name) || string.IsNullOrWhiteSpace(existing.Name)) return false;
        var sameText = string.Equals(Normalize(candidate.Name), Normalize(existing.Name), StringComparison.OrdinalIgnoreCase);
        return sameText && (candidate.Bounds is null || existing.Bounds is null || Intersects(candidate.Bounds, existing.Bounds));
    }

    private static int StructuralPriority(SemanticElement element) => element.Type switch
    {
        "input" or "edit" or "textbox" or "document" or "combobox" => 5,
        "button" or "link" or "checkbox" or "radio" or "tab" or "menu_item" => 4,
        "list_item" or "tree_item" => 3,
        "text" => 1,
        _ => 2
    };

    private static string Normalize(string value) => string.Join(' ', value.Split((char[]?)null,
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static bool Intersects(Bounds first, Bounds second) =>
        first.X < second.X + second.Width && first.X + first.Width > second.X &&
        first.Y < second.Y + second.Height && first.Y + first.Height > second.Y;
}

public sealed class PerceptionUnavailableException(string message) : Exception(message);
