namespace Freely.Perception.Models;

public enum PerceptionTargetKind { ActiveWindow, File }
public enum ObservationDetail { Compact, Normal, Full }
public enum ObservationConfidence { Low, Medium, High }
public enum PerceptionProviderKind { Structured, VisualFallback, Content }

public sealed record PerceptionTarget(PerceptionTargetKind Kind, string? Location = null);

public sealed record ObservationOptions(
    ObservationDetail Detail = ObservationDetail.Normal,
    int MaxElements = 160,
    int MaxTextCharacters = 50_000);

public sealed record Bounds(double X, double Y, double Width, double Height);

public sealed record SemanticElement(
    string Id,
    string Type,
    string Name,
    string? Value,
    string? Description,
    bool? Enabled,
    bool? Selected,
    string? ParentId,
    Bounds? Bounds,
    ObservationConfidence Confidence,
    IReadOnlyList<string>? Actions = null,
    IReadOnlyDictionary<string, string>? Identifiers = null,
    bool InViewport = true,
    bool Offscreen = false,
    bool Focused = false,
    bool? Checked = null,
    bool? Expanded = null,
    bool? ReadOnly = null,
    bool Sensitive = false,
    string CoordinateSpace = "physical_screen",
    string? SourceType = null)
{
    public IReadOnlyList<string> SupportedActions { get; } = Actions ?? [];
    public IReadOnlyDictionary<string, string> StableIdentifiers { get; } = Identifiers ??
        new Dictionary<string, string>();
}

public sealed record Observation(
    string Source,
    string Type,
    string Title,
    string PlainText,
    IReadOnlyList<SemanticElement> Elements,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset Timestamp,
    bool IsSufficient = true,
    long Revision = 0);

/// <summary>
/// Canonical, platform-neutral UI tree consumed by the agent. Nodes are kept flat with ParentId so
/// snapshots stay compact and can later be updated with patches without repeatedly nesting JSON.
/// </summary>
public sealed record XTreeSnapshot(
    long Revision,
    string TargetType,
    string Title,
    IReadOnlyList<SemanticElement> Nodes,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset Timestamp,
    bool IsSufficient,
    string? Text = null);

public enum XTreePatchOperation { Add, Update, Remove }

public sealed record XTreePatch(
    long BaseRevision,
    long Revision,
    IReadOnlyList<XTreePatchEntry> Entries,
    DateTimeOffset Timestamp);

public sealed record XTreePatchEntry(XTreePatchOperation Operation, string NodeId, SemanticElement? Node = null);

public static class XTreeConversions
{
    public static XTreeSnapshot ToXTree(this Observation observation) => new(
        observation.Revision,
        observation.Type,
        observation.Title,
        observation.Elements,
        observation.Metadata,
        observation.Timestamp,
        observation.IsSufficient,
        observation.PlainText);
}
