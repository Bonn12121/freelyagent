using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Freely.Agent.Models;
using Freely.Agent.Runtime;

namespace Freely.AI.Providers;

public sealed class OpenAiCompatibleProvider : IModelProvider
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiCompatibleOptions _options;

    public OpenAiCompatibleProvider(HttpClient httpClient, OpenAiCompatibleOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public string Id => _options.ProviderId;
    public string DisplayName => _options.DisplayName;

    public async IAsyncEnumerable<ModelStreamChunk> StreamAsync(
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.BaseUri, "chat/completions"));
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        var messages = request.Messages.Select(item => new Dictionary<string, string>
        {
            ["role"] = item.Role switch
            {
                MessageRole.Assistant => "assistant",
                MessageRole.User => "user",
                _ => "system"
            },
            ["content"] = item.Role == MessageRole.Tool ? $"Tool observation: {item.Content}" : item.Content
        }).ToList();
        if (!_options.SupportsNativeTools)
        {
            messages.Insert(0, new Dictionary<string, string>
            {
                ["role"] = "system",
                ["content"] = ProtocolToolCallParser.BuildInstruction(request.Tools)
            });
        }

        var payload = new Dictionary<string, object?>
        {
            ["model"] = _options.Model,
            ["stream"] = true,
            ["messages"] = messages
        };
        if (_options.SupportsNativeTools)
        {
            payload["tools"] = request.Tools.Select(tool => new
            {
                type = "function",
                function = new
                {
                    name = tool.Name,
                    description = tool.Description,
                    parameters = JsonSerializer.Deserialize<JsonElement>(tool.ParametersJsonSchema)
                }
            }).ToArray();
        }

        message.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var toolId = new StringBuilder();
        var toolName = new StringBuilder();
        var toolArguments = new StringBuilder();
        var protocolResponse = new StringBuilder();

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") break;

            using var document = JsonDocument.Parse(data);
            if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;
            var choice = choices[0];
            if (!choice.TryGetProperty("delta", out var delta)) continue;
            if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            {
                if (_options.SupportsNativeTools) yield return new ModelStreamChunk(content.GetString());
                else protocolResponse.Append(content.GetString());
            }

            if (delta.TryGetProperty("tool_calls", out var calls) && calls.GetArrayLength() > 0)
            {
                var call = calls[0];
                if (call.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String) toolId.Append(id.GetString());
                if (call.TryGetProperty("function", out var function))
                {
                    if (function.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String) toolName.Append(name.GetString());
                    if (function.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.String) toolArguments.Append(args.GetString());
                }
            }
        }

        if (!_options.SupportsNativeTools)
        {
            if (ProtocolToolCallParser.TryParse(protocolResponse.ToString(), out var protocolCall))
            {
                yield return new ModelStreamChunk(ToolCall: protocolCall, IsComplete: true);
            }
            else
            {
                yield return new ModelStreamChunk(protocolResponse.ToString(), IsComplete: true);
            }
            yield break;
        }

        if (toolName.Length > 0)
        {
            yield return new ModelStreamChunk(
                ToolCall: new ToolCall(toolId.Length > 0 ? toolId.ToString() : Guid.NewGuid().ToString("N"), toolName.ToString(), toolArguments.ToString()),
                IsComplete: true);
        }
        else
        {
            yield return new ModelStreamChunk(IsComplete: true);
        }
    }
}
