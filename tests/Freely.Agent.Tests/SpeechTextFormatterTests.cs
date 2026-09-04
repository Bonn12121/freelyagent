using Freely.Voice;
using Xunit;

namespace Freely.Agent.Tests;

public sealed class SpeechTextFormatterTests
{
    [Fact]
    public void RemovesCodeAndUrlsButKeepsReadableLinkText()
    {
        const string markdown = "Done. [Open the report](https://example.com/report). ```json { \"secret\": true } ```";

        var speech = SpeechTextFormatter.ForSpeech(markdown);

        Assert.Contains("Open the report", speech, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", speech, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", speech, StringComparison.Ordinal);
    }
}
