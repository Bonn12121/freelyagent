using Freely.Agent.Models;
using Freely.Agent.Runtime;
using Freely.Computer;
using Freely.Tools.Browser;
using Xunit;

namespace Freely.Agent.Tests;

public sealed class ControlRuntimeTests
{
    [Fact]
    public async Task EmergencyStopCancelsActiveComputerAction()
    {
        var coordinator = new ComputerControlCoordinator();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notified = false;
        coordinator.EmergencyStopped += (_, _) => notified = true;
        var action = coordinator.RunExclusiveAsync(async token =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return true;
        }, CancellationToken.None);

        await started.Task;
        coordinator.ForceStop();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => action);
        Assert.True(notified);
    }

    [Fact]
    public async Task BrowserClickReturnsPostActionObservation()
    {
        var browser = new FakeBrowserSession();
        var tool = new BrowserClickTool(browser);
        var result = await tool.ExecuteAsync(new ToolCall("1", "browser.click", "{\"elementId\":\"web_7\"}"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(browser.VisibleRequested);
        Assert.Equal("web_7", browser.ClickedId);
        Assert.Contains("after-click", result.Output);
        Assert.Equal(ToolRisk.Write, tool.Risk);
    }

    private sealed class FakeBrowserSession : IBrowserSession
    {
        public bool IsReady => true;
        public string? ClickedId { get; private set; }
        public bool VisibleRequested { get; private set; }
        public event EventHandler<bool>? VisibilityRequested;
        public Task<string> OpenAsync(Uri uri, CancellationToken cancellationToken) => Task.FromResult("opened");
        public Task<string> SnapshotAsync(CancellationToken cancellationToken) => Task.FromResult("snapshot");
        public Task<string> ClickAsync(string elementId, CancellationToken cancellationToken)
        {
            ClickedId = elementId;
            return Task.FromResult("after-click observation");
        }
        public Task<string> TypeAsync(string elementId, string text, bool submit, CancellationToken cancellationToken) => Task.FromResult("after-type observation");
        public Task<string> ScrollAsync(int delta, CancellationToken cancellationToken) => Task.FromResult("after-scroll observation");
        public Task<string> PressAsync(string keys, CancellationToken cancellationToken) => Task.FromResult("after-key observation");
        public void RequestVisibility(bool visible)
        {
            VisibleRequested = visible;
            VisibilityRequested?.Invoke(this, visible);
        }
    }
}
