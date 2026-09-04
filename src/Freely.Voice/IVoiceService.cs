namespace Freely.Voice;

public sealed record VoiceChoice(string Id, string Name, string Language, bool IsMale)
{
    public string DisplayName => $"{Name} — {Language}";
}

public interface IVoiceService : IDisposable
{
    IReadOnlyList<VoiceChoice> AvailableVoices { get; }
    string? SelectedVoiceId { get; }
    bool IsSpeaking { get; }
    void SelectVoice(string? voiceId);
    Task SpeakAsync(string text, CancellationToken cancellationToken = default);
    void Stop();
}

public interface IDictationService : IDisposable
{
    bool IsListening { get; }
    Task<string> ListenOnceAsync(CancellationToken cancellationToken = default);
    void Stop();
}
