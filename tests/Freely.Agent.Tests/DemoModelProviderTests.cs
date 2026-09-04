using Freely.AI.Providers;
using Freely.Agent.Models;
using Xunit;

namespace Freely.Agent.Tests;

public sealed class DemoModelProviderTests
{
    [Theory]
    [InlineData("list the files in my Downloads folder", "folder.list")]
    [InlineData("read C:\\notes.txt", "file.read")]
    [InlineData("open example.com", "browser.open")]
    [InlineData("open Notepad", "app.launch")]
    [InlineData("launch Discord", "app.launch")]
    [InlineData("Go to Discord and change theme to Light", "app.launch")]
    [InlineData("search the web for WinUI", "browser.open")]
    [InlineData("type hello world", "computer.keyboard_type")]
    [InlineData("press ctrl+l", "computer.keyboard_press")]
    [InlineData("scroll page down", "browser.scroll")]
    [InlineData("press tab on the page", "browser.key_press")]
    [InlineData("run Get-Date in PowerShell", "shell.powershell")]
    [InlineData("what is on my screen", "perception.read")]
    [InlineData("analyze file C:\\report.txt", "perception.read")]
    public async Task UnderstandsNaturalLanguageActions(string goal, string expectedTool)
    {
        var provider = new DemoModelProvider();
        var request = new AgentRequest(goal, [new AgentMessage(MessageRole.User, goal)], []);
        ToolCall? toolCall = null;

        await foreach (var chunk in provider.StreamAsync(request, CancellationToken.None))
        {
            toolCall ??= chunk.ToolCall;
        }

        Assert.NotNull(toolCall);
        Assert.Equal(expectedTool, toolCall.Name);
    }
}
