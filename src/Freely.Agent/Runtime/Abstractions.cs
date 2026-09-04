using Freely.Agent.Models;

namespace Freely.Agent.Runtime;

public interface IModelProvider
{
    string Id { get; }
    string DisplayName { get; }
    IAsyncEnumerable<ModelStreamChunk> StreamAsync(AgentRequest request, CancellationToken cancellationToken);
}

public interface IAgentTool
{
    ToolDefinition Definition { get; }
    ToolRisk Risk { get; }
    Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken);
}

public enum ToolRisk { ReadOnly, Write, SystemChanging, Administrator, Destructive, Unknown }

public sealed record PermissionDecision(bool Allowed, bool RequiresConfirmation, string Reason);

public interface IPermissionGate
{
    Task<PermissionDecision> CheckAsync(IAgentTool tool, ToolCall call, CancellationToken cancellationToken);
}

public interface IAgentRuntime
{
    AgentStatus Status { get; }
    event EventHandler<AgentProgress>? ProgressChanged;
    IAsyncEnumerable<string> RunAsync(string goal, CancellationToken cancellationToken);
}

