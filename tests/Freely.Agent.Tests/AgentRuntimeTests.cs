using Freely.Agent.Models;
using Freely.Agent.Runtime;
using Xunit;

namespace Freely.Agent.Tests;

public sealed class AgentRuntimeTests
{
    [Fact]
    public async Task StreamsAnswerAndCompletes()
    {
        var runtime = new AgentRuntime(new TextProvider("hello"), new AllowGate(), []);
        var chunks = new List<string>();

        await foreach (var chunk in runtime.RunAsync("test", CancellationToken.None)) chunks.Add(chunk);

        Assert.Equal("hello", string.Concat(chunks));
        Assert.Equal(AgentStatus.Completed, runtime.Status);
    }

    [Fact]
    public async Task StopsWhenPermissionIsDenied()
    {
        var tool = new StubTool();
        var runtime = new AgentRuntime(new ToolProvider(), new DenyGate(), [tool]);
        var chunks = new List<string>();

        await foreach (var chunk in runtime.RunAsync("test", CancellationToken.None)) chunks.Add(chunk);

        Assert.False(tool.Executed);
        Assert.Equal(AgentStatus.WaitingForPermission, runtime.Status);
        Assert.Contains("paused", string.Concat(chunks), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HidesInternalToolObservationTextFromTheUser()
    {
        var runtime = new AgentRuntime(
            new TextProvider("system\nTool observation: {\"ToolCallId\":\"secret\",\"ToolName\":\"perception.read\"}"),
            new AllowGate(), []);
        var chunks = new List<string>();

        await foreach (var chunk in runtime.RunAsync("test", CancellationToken.None)) chunks.Add(chunk);

        Assert.Equal("Done.", string.Concat(chunks));
        Assert.DoesNotContain("ToolCallId", string.Concat(chunks), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReusesAReadOnlyObservationUntilAWriteChangesTheUi()
    {
        var tool = new ReadOnlyTool();
        var runtime = new AgentRuntime(new RepeatedReadProvider(), new AllowGate(), [tool]);
        var chunks = new List<string>();

        await foreach (var chunk in runtime.RunAsync("test", CancellationToken.None)) chunks.Add(chunk);

        Assert.Equal(1, tool.ExecutionCount);
        Assert.Equal("finished", string.Concat(chunks));
    }

    [Fact]
    public async Task DoesNotRescanAfterNativeActionAlreadyReturnedFreshScreen()
    {
        var action = new NativeActionTool();
        var read = new PerceptionTool();
        var runtime = new AgentRuntime(new NativeActionThenReadProvider(), new AllowGate(), [action, read]);
        var chunks = new List<string>();

        await foreach (var chunk in runtime.RunAsync("test", CancellationToken.None)) chunks.Add(chunk);

        Assert.Equal(1, action.ExecutionCount);
        Assert.Equal(0, read.ExecutionCount);
        Assert.Equal("finished", string.Concat(chunks));
    }

    private sealed class TextProvider(string text) : IModelProvider
    {
        public string Id => "test";
        public string DisplayName => "Test";
        public async IAsyncEnumerable<ModelStreamChunk> StreamAsync(AgentRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return new ModelStreamChunk(text, IsComplete: true);
        }
    }

    private sealed class ToolProvider : IModelProvider
    {
        public string Id => "test";
        public string DisplayName => "Test";
        public async IAsyncEnumerable<ModelStreamChunk> StreamAsync(AgentRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return new ModelStreamChunk(ToolCall: new ToolCall("1", "test.tool", "{}"), IsComplete: true);
        }
    }

    private sealed class RepeatedReadProvider : IModelProvider
    {
        private int _turn;
        public string Id => "test";
        public string DisplayName => "Test";
        public async IAsyncEnumerable<ModelStreamChunk> StreamAsync(AgentRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (_turn++ < 2)
                yield return new ModelStreamChunk(ToolCall: new ToolCall(_turn.ToString(), "test.read", "{}"), IsComplete: true);
            else
                yield return new ModelStreamChunk("finished", IsComplete: true);
        }
    }

    private sealed class NativeActionThenReadProvider : IModelProvider
    {
        private int _turn;
        public string Id => "test";
        public string DisplayName => "Test";
        public async IAsyncEnumerable<ModelStreamChunk> StreamAsync(AgentRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return _turn++ switch
            {
                0 => new ModelStreamChunk(ToolCall: new ToolCall("1", "app.click_element", "{\"elementId\":\"button\"}"), IsComplete: true),
                1 => new ModelStreamChunk(ToolCall: new ToolCall("2", "perception.read", "{\"target\":\"active_window\"}"), IsComplete: true),
                _ => new ModelStreamChunk("finished", IsComplete: true)
            };
        }
    }

    private sealed class StubTool : IAgentTool
    {
        public bool Executed { get; private set; }
        public ToolDefinition Definition => new("test.tool", "test", "{\"type\":\"object\"}");
        public ToolRisk Risk => ToolRisk.Write;
        public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
        {
            Executed = true;
            return Task.FromResult(new ToolResult(call.Id, call.Name, true, "ok"));
        }
    }

    private sealed class ReadOnlyTool : IAgentTool
    {
        public int ExecutionCount { get; private set; }
        public ToolDefinition Definition => new("test.read", "test", "{\"type\":\"object\"}");
        public ToolRisk Risk => ToolRisk.ReadOnly;
        public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return Task.FromResult(new ToolResult(call.Id, call.Name, true, "screen observation"));
        }
    }

    private sealed class NativeActionTool : IAgentTool
    {
        public int ExecutionCount { get; private set; }
        public ToolDefinition Definition => new("app.click_element", "test", "{\"type\":\"object\"}");
        public ToolRisk Risk => ToolRisk.Write;
        public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return Task.FromResult(new ToolResult(call.Id, call.Name, true, "<observation source=\"test\">fresh screen</observation>"));
        }
    }

    private sealed class PerceptionTool : IAgentTool
    {
        public int ExecutionCount { get; private set; }
        public ToolDefinition Definition => new("perception.read", "test", "{\"type\":\"object\"}");
        public ToolRisk Risk => ToolRisk.ReadOnly;
        public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return Task.FromResult(new ToolResult(call.Id, call.Name, true, "screen"));
        }
    }

    private sealed class AllowGate : IPermissionGate
    {
        public Task<PermissionDecision> CheckAsync(IAgentTool tool, ToolCall call, CancellationToken cancellationToken) => Task.FromResult(new PermissionDecision(true, false, ""));
    }

    private sealed class DenyGate : IPermissionGate
    {
        public Task<PermissionDecision> CheckAsync(IAgentTool tool, ToolCall call, CancellationToken cancellationToken) => Task.FromResult(new PermissionDecision(false, false, "denied"));
    }
}
