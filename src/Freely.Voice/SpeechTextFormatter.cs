using System.Text.RegularExpressions;

namespace Freely.Voice;

public static partial class SpeechTextFormatter
{
    public static string ForSpeech(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;
        var text = CodeBlock().Replace(markdown, " ");
        text = MarkdownLink().Replace(text, "$1");
        text = Url().Replace(text, " ");
        text = MarkdownSyntax().Replace(text, " ");
        text = Whitespace().Replace(text, " ").Trim();
        return text.Length > 2_400 ? text[..2_400] + ". The rest is available on screen." : text;
    }

    [GeneratedRegex("```[\\s\\S]*?```", RegexOptions.Compiled)]
    private static partial Regex CodeBlock();

    [GeneratedRegex("\\[([^\\]]+)\\]\\([^)]+\\)", RegexOptions.Compiled)]
    private static partial Regex MarkdownLink();

    [GeneratedRegex("https?://\\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex Url();

    [GeneratedRegex("[#*_>`|~-]+", RegexOptions.Compiled)]
    private static partial Regex MarkdownSyntax();

    [GeneratedRegex("\\s+", RegexOptions.Compiled)]
    private static partial Regex Whitespace();
}

