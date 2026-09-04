using Freely.Agent.Models;
using Freely.Agent.Runtime;
using Freely.Security.Permissions;
using Xunit;

namespace Freely.Security.Tests;

public sealed class PermissionGateTests
{
    [Fact]
    public async Task DestructiveActionCannotBypassConfirmation()
    {
        var policy = new PermissionPolicy { ReadOnly = PermissionLevel.Allow, Destructive = PermissionLevel.Ask };
        var gate = new PermissionGate(policy);

        var decision = await gate.CheckAsync(new DestructiveTool(), new ToolCall("1", "danger", "{}"), CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.True(decision.RequiresConfirmation);
    }

    [Fact]
    public async Task UserCanApproveAskActionOnce()
    {
        var gate = new PermissionGate(new PermissionPolicy { Write = PermissionLevel.Ask });
        gate.PermissionRequested += (_, request) => request.Resolve(true);

        var decision = await gate.CheckAsync(new WriteTool(), new ToolCall("1", "write", "{}"), CancellationToken.None);

        Assert.True(decision.Allowed);
        Assert.True(decision.RequiresConfirmation);
    }

    [Fact]
    public async Task ToolOverrideCanAlwaysAllowAndReturnToDefault()
    {
        var policy = new PermissionPolicy { Write = PermissionLevel.Ask };
        var gate = new PermissionGate(policy);
        var tool = new WriteTool();
        policy.SetOverride(tool.Definition.Name, PermissionLevel.Allow);

        var allowed = await gate.CheckAsync(tool, new ToolCall("1", tool.Definition.Name, "{}"), CancellationToken.None);
        policy.ClearOverride(tool.Definition.Name);
        var restored = await gate.CheckAsync(tool, new ToolCall("2", tool.Definition.Name, "{}"), CancellationToken.None);

        Assert.True(allowed.Allowed);
        Assert.False(allowed.RequiresConfirmation);
        Assert.False(restored.Allowed);
        Assert.True(restored.RequiresConfirmation);
    }

    [Fact]
    public async Task NativeAppControlProfileAllowsEveryControlStepWithoutConfirmation()
    {
        var policy = new PermissionPolicy { Write = PermissionLevel.Ask, SystemChanging = PermissionLevel.Ask };
        var gate = new PermissionGate(policy);
        NativeAppControlPermissions.Apply(policy, true);

        foreach (var toolName in NativeAppControlPermissions.ToolNames)
        {
            var risk = toolName == "app.launch" ? ToolRisk.SystemChanging : ToolRisk.Write;
            var decision = await gate.CheckAsync(new NamedTool(toolName, risk), new ToolCall(toolName, toolName, "{}"), CancellationToken.None);

            Assert.True(decision.Allowed);
            Assert.False(decision.RequiresConfirmation);
        }
    }

    [Fact]
    public async Task DisablingNativeAppControlProfileRestoresConfirmation()
    {
        var policy = new PermissionPolicy { Write = PermissionLevel.Ask, SystemChanging = PermissionLevel.Ask };
        var gate = new PermissionGate(policy);
        NativeAppControlPermissions.Apply(policy, true);
        NativeAppControlPermissions.Apply(policy, false);

        var decision = await gate.CheckAsync(new NamedTool("app.launch", ToolRisk.SystemChanging), new ToolCall("1", "app.launch", "{}"), CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.True(decision.RequiresConfirmation);
    }

    private sealed class DestructiveTool : StubTool { public override ToolRisk Risk => ToolRisk.Destructive; }
    private sealed class WriteTool : StubTool { public override ToolRisk Risk => ToolRisk.Write; }

    private sealed class NamedTool(string name, ToolRisk risk) : IAgentTool
    {
        public ToolDefinition Definition { get; } = new(name, "test", "{\"type\":\"object\"}");
        public ToolRisk Risk { get; } = risk;
        public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken) => Task.FromResult(new ToolResult(call.Id, call.Name, true, ""));
    }

    private abstract class StubTool : IAgentTool
    {
        public ToolDefinition Definition => new(GetType().Name, "test", "{\"type\":\"object\"}");
        public abstract ToolRisk Risk { get; }
        public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken) => Task.FromResult(new ToolResult(call.Id, call.Name, true, ""));
    }
}
