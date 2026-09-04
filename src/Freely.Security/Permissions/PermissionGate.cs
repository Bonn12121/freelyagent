using Freely.Agent.Models;
using Freely.Agent.Runtime;

namespace Freely.Security.Permissions;

public sealed class PermissionRequestEventArgs(IAgentTool tool, ToolCall call) : EventArgs
{
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IAgentTool Tool { get; } = tool;
    public ToolCall Call { get; } = call;
    public Task<bool> Decision => _completion.Task;
    public void Resolve(bool allowed) => _completion.TrySetResult(allowed);
}

public sealed class PermissionGate(PermissionPolicy policy) : IPermissionGate
{
    public event EventHandler<PermissionRequestEventArgs>? PermissionRequested;

    public async Task<PermissionDecision> CheckAsync(
        IAgentTool tool,
        ToolCall call,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var level = policy.GetLevel(tool.Definition.Name, tool.Risk);
        if (level == PermissionLevel.Allow)
        {
            return new PermissionDecision(true, false, "Allowed by policy.");
        }

        if (level == PermissionLevel.Deny)
        {
            return new PermissionDecision(false, false, $"{tool.Definition.Name} is denied in Settings.");
        }

        var handler = PermissionRequested;
        if (handler is null)
        {
            return new PermissionDecision(false, true, $"{tool.Definition.Name} requires your confirmation ({tool.Risk}).");
        }

        var request = new PermissionRequestEventArgs(tool, call);
        handler(this, request);
        var allowed = await request.Decision.WaitAsync(cancellationToken).ConfigureAwait(false);

        return allowed
            ? new PermissionDecision(true, true, "Allowed once by the user.")
            : new PermissionDecision(false, true, $"You declined {tool.Definition.Name}.");
    }
}
