namespace Freely.AI.Providers;

public sealed record OpenAiCompatibleOptions(
    string ProviderId,
    string DisplayName,
    Uri BaseUri,
    string Model,
    string? ApiKey = null,
    bool SupportsNativeTools = true);

