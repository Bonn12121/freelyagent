using Freely.Perception.Models;

namespace Freely.Perception;

public interface IPerceptionProvider
{
    int Priority { get; }
    PerceptionProviderKind Kind { get; }
    bool CanHandle(PerceptionTarget target);
    Task<Observation> ObserveAsync(PerceptionTarget target, ObservationOptions options, CancellationToken cancellationToken);
}

public sealed class PerceptionManager(IEnumerable<IPerceptionProvider> providers)
{
    private readonly IReadOnlyList<IPerceptionProvider> _providers = providers.OrderByDescending(provider => provider.Priority).ToArray();
    private long _revision;

    public async Task<Observation> ObserveAsync(
        PerceptionTarget target,
        ObservationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedOptions = options ?? new ObservationOptions();
        if (target.Kind != PerceptionTargetKind.ActiveWindow)
        {
            var content = await CaptureFirstAsync(target, resolvedOptions,
                _providers.Where(provider => provider.Kind == PerceptionProviderKind.Content), cancellationToken).ConfigureAwait(false);
            return content is null
                ? throw new PerceptionUnavailableException($"No perception provider could read {target.Kind}.")
                : WithRoutingMetadata(content, "content_reader", false, null, 1d);
        }

        var structured = await CaptureFirstAsync(target, resolvedOptions,
            _providers.Where(provider => provider.Kind == PerceptionProviderKind.Structured), cancellationToken).ConfigureAwait(false);
        var assessment = AssessStructure(structured);
        if (structured is not null && assessment.IsSufficient)
            return WithRoutingMetadata(structured, "xtree_primary", false, null, assessment.Score);

        var visual = await CaptureFirstAsync(target, resolvedOptions,
            _providers.Where(provider => provider.Kind == PerceptionProviderKind.VisualFallback), cancellationToken).ConfigureAwait(false);
        if (structured is not null && visual is not null)
            return WithRoutingMetadata(Fuse(structured, visual, resolvedOptions), "xtree_with_ocr_fallback", true,
                assessment.Reason, Math.Max(assessment.Score, visual.IsSufficient ? 0.55d : 0.25d));
        if (structured is not null)
            return WithRoutingMetadata(structured, "xtree_primary_degraded", false, assessment.Reason, assessment.Score);
        if (visual is not null)
            return WithRoutingMetadata(visual, "ocr_fallback_only", true, "structured_provider_unavailable", 0.5d);
        throw new PerceptionUnavailableException($"No perception provider could read {target.Kind}.");
    }

    private static async Task<Observation?> CaptureFirstAsync(
        PerceptionTarget target,
        ObservationOptions options,
        IEnumerable<IPerceptionProvider> providers,
        CancellationToken cancellationToken)
    {
        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!provider.CanHandle(target)) continue;
            try
            {
                return await provider.ObserveAsync(target, options, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is PerceptionUnavailableException or InvalidOperationException or
                System.Runtime.InteropServices.ExternalException or UnauthorizedAccessException or System.IO.FileNotFoundException)
            {
                // Try the next adapter in the same tier. OCR is never entered from this loop unless its tier was requested.
            }
        }
        return null;
    }

    private static StructuralAssessment AssessStructure(Observation? observation)
    {
        if (observation is null) return new(false, 0d, "structured_provider_unavailable");
        if (!observation.IsSufficient) return new(false, 0.2d, "structured_provider_reported_insufficient");
        var named = observation.Elements.Count(element => !string.IsNullOrWhiteSpace(element.Name));
        var actionable = observation.Elements.Count(element => !string.IsNullOrWhiteSpace(element.Name) &&
            (element.SupportedActions.Count > 0 || IsActionableRole(element.Type)));
        var highConfidence = observation.Elements.Count(element => element.Confidence == ObservationConfidence.High);
        var score = Math.Min(0.99d, 0.35d + Math.Min(named, 12) * 0.035d + Math.Min(actionable, 5) * 0.08d +
            Math.Min(highConfidence, 10) * 0.012d);
        if (actionable > 0) return new(true, Math.Max(0.8d, score), string.Empty);
        if (named >= 6) return new(true, Math.Max(0.72d, score), string.Empty);
        return new(false, score, named == 0 ? "structured_tree_has_no_named_nodes" : "structured_tree_lacks_actionable_context");
    }

    private static bool IsActionableRole(string role) => role is
        "input" or "edit" or "textbox" or "document" or "combobox" or "button" or "link" or
        "checkbox" or "radio" or "tab" or "menuitem" or "menu_item" or "listitem" or "list_item" or
        "treeitem" or "tree_item" or "slider" or "switch";

    private Observation WithRoutingMetadata(
        Observation observation,
        string mode,
        bool usedOcr,
        string? fallbackReason,
        double score)
    {
        var revision = Interlocked.Increment(ref _revision);
        var metadata = new Dictionary<string, string>(observation.Metadata)
        {
            ["perceptionMode"] = mode,
            ["ocrFallbackUsed"] = usedOcr ? "true" : "false",
            ["structuralConfidence"] = score.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            ["revision"] = revision.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(fallbackReason)) metadata["fallbackReason"] = fallbackReason;
        return observation with { Metadata = metadata, Revision = revision };
    }

    private static Observation Fuse(Observation structured, Observation visual, ObservationOptions options)
    {
        var elements = new List<SemanticElement>(Math.Min(options.MaxElements, structured.Elements.Count + visual.Elements.Count));
        var passwordBounds = structured.Elements
            .Where(item => item.Sensitive && item.Bounds is not null)
            .Select(item => item.Bounds!)
            .ToArray();
        foreach (var candidate in structured.Elements.OrderByDescending(StructuralPriority))
        {
            if (elements.Count >= options.MaxElements) break;
            if (elements.Any(existing => IsDuplicate(existing, candidate))) continue;
            elements.Add(candidate);
        }
        foreach (var candidate in visual.Elements)
        {
            if (elements.Count >= options.MaxElements) break;
            if (candidate.Bounds is { } visualBounds && passwordBounds.Any(password => Intersects(password, visualBounds))) continue;
            if (elements.Any(existing => IsDuplicate(existing, candidate))) continue;
            elements.Add(candidate);
        }

        var plainTextParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(structured.PlainText)) plainTextParts.Add(structured.PlainText);
        if (!string.IsNullOrWhiteSpace(visual.PlainText)) plainTextParts.Add("OCR FALLBACK NODES:\n" + visual.PlainText);
        var plainText = string.Join("\n\n", plainTextParts);
        if (plainText.Length > options.MaxTextCharacters) plainText = plainText[..options.MaxTextCharacters];

        var metadata = new Dictionary<string, string>(structured.Metadata)
        {
            ["sources"] = $"{structured.Source},{visual.Source}",
            ["fusion"] = "xtree_plus_regional_ocr_fallback",
            ["imageSharedWithModel"] = "false",
            ["elementCount"] = elements.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        return new Observation("unified_xtree", structured.Type, structured.Title, plainText, elements, metadata,
            DateTimeOffset.UtcNow, structured.IsSufficient || visual.IsSufficient);
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

    private sealed record StructuralAssessment(bool IsSufficient, double Score, string Reason);
}

public sealed class PerceptionUnavailableException(string message) : Exception(message);
