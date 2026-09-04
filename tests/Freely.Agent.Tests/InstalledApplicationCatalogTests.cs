using Freely.Computer;
using Xunit;

namespace Freely.Agent.Tests;

public sealed class InstalledApplicationCatalogTests
{
    [Fact]
    public void FindsBuiltInApplicationByFriendlyName()
    {
        var catalog = new InstalledApplicationCatalog();

        var match = Assert.Single(catalog.Search("Notepad").Where(item => item.Score == 100).Take(1));

        Assert.Equal("Notepad", match.Application.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(match.Application.LaunchTarget));
    }
}
