namespace Freely.Agent.Models;

public enum AgentStatus
{
    Idle,
    Thinking,
    Planning,
    Acting,
    WaitingForPermission,
    Verifying,
    Completed,
    Failed,
    Stopped
}

public enum MessageRole { System, User, Assistant, Tool }

public sealed record AgentMessage(MessageRole Role, string Content);

public sealed record AgentRequest(
    string Goal,
    IReadOnlyList<AgentMessage> Messages,
    IReadOnlyList<ToolDefinition> Tools);

public sealed record ToolDefinition(string Name, string Description, string ParametersJsonSchema);

public sealed record ModelStreamChunk(
    string? Text = null,
    ToolCall? ToolCall = null,
    bool IsComplete = false);

public sealed record ToolCall(string Id, string Name, string ArgumentsJson);

public sealed record ToolResult(
    string ToolCallId,
    string ToolName,
    bool Success,
    string Output,
    string? Error = null);

public sealed record AgentProgress(AgentStatus Status, string Message, ToolResult? ToolResult = null);

