using System.IO;
using System.Text.Json;
using Freely.Perception.Models;

namespace Freely.Perception.Providers;

public sealed class FilePerceptionProvider : IPerceptionProvider
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".json", ".jsonl", ".xml", ".yaml", ".yml", ".csv", ".tsv",
        ".cs", ".csproj", ".sln", ".xaml", ".js", ".jsx", ".ts", ".tsx", ".css", ".html",
        ".py", ".java", ".cpp", ".h", ".rs", ".go", ".sql", ".log"
    };

    public int Priority => 80;
    public bool CanHandle(PerceptionTarget target) => target.Kind == PerceptionTargetKind.File && !string.IsNullOrWhiteSpace(target.Location);

    public async Task<Observation> ObserveAsync(PerceptionTarget target, ObservationOptions options, CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(target.Location!);
        if (!File.Exists(path)) throw new FileNotFoundException("The requested file does not exist.", path);
        var info = new FileInfo(path);
        var extension = info.Extension;
        var metadata = new Dictionary<string, string>
        {
            ["path"] = path,
            ["extension"] = extension,
            ["sizeBytes"] = info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["lastModifiedUtc"] = info.LastWriteTimeUtc.ToString("O")
        };

        if (!TextExtensions.Contains(extension))
        {
            return new Observation("file_metadata", "file", info.Name,
                "This file format needs a specialized parser. Metadata is available, but its contents were not decoded.",
                [], metadata, DateTimeOffset.UtcNow, false);
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 16_384, true);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[options.MaxTextCharacters];
        var count = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
        var text = new string(buffer, 0, count);
        if (!reader.EndOfStream) text += "\n[Content truncated by perception budget]";
        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase)) text = NormalizeJson(text);

        return new Observation("file_reader", "document", info.Name, text, [], metadata, DateTimeOffset.UtcNow);
    }

    private static string NormalizeJson(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return text;
        }
    }
}
