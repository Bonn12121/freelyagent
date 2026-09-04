namespace Freely.AI.Providers;

public sealed record ProviderPreset(
    string Id,
    string DisplayName,
    string Endpoint,
    string DefaultModel,
    bool IsLocal,
    bool RequiresApiKey);

public static class ProviderCatalog
{
    public static IReadOnlyList<ProviderPreset> All { get; } =
    [
        new("demo", "Demo (no model)", "", "", true, false),
        new("ollama", "Ollama / local compatible", "http://localhost:11434/v1/", "llama3.2", true, false),
        new("openrouter", "OpenRouter", "https://openrouter.ai/api/v1/", "~openai/gpt-latest", false, true),
        new("xai", "xAI Console", "https://api.x.ai/v1/", "grok-4.6", false, true),
        new("ai-studio", "Google AI Studio", "https://generativelanguage.googleapis.com/v1beta/openai/", "gemini-3.7-flash", false, true),
        new("nvidia-nim", "NVIDIA NIM", "https://integrate.api.nvidia.com/v1/", "openai/gpt-oss-120b", false, true),
        new("openai-compatible", "Custom OpenAI-compatible", "https://example.com/v1/", "model-id", false, true)
    ];

    public static ProviderPreset Get(string id) =>
        All.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}

