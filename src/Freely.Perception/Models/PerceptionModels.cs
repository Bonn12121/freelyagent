namespace Freely.Perception.Models;

public enum PerceptionTargetKind { ActiveWindow, File }
public enum ObservationDetail { Compact, Normal, Full }
public enum ObservationConfidence { Low, Medium, High }

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
    ObservationConfidence Confidence);

public sealed record Observation(
    string Source,
    string Type,
    string Title,
    string PlainText,
    IReadOnlyList<SemanticElement> Elements,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset Timestamp,
    bool IsSufficient = true);

