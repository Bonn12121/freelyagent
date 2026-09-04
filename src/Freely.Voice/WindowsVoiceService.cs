using System.Globalization;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace Freely.Voice;

public sealed class WindowsVoiceService : IVoiceService
{
    private readonly object _sync = new();
    private readonly MediaPlayer _player = new() { AudioCategory = MediaPlayerAudioCategory.Speech };
    private SpeechSynthesisStream? _activeStream;
    private string? _selectedVoiceId;

    public WindowsVoiceService()
    {
        AvailableVoices = SpeechSynthesizer.AllVoices
            .Select(voice => new VoiceChoice(voice.Id, voice.DisplayName, voice.Language, voice.Gender == VoiceGender.Male))
            .OrderByDescending(voice => voice.IsMale)
            .ThenBy(voice => voice.Language)
            .ThenBy(voice => voice.Name)
            .ToArray();
        _player.MediaEnded += (_, _) => ReleaseStream();
        _player.MediaFailed += (_, _) => ReleaseStream();
    }

    public IReadOnlyList<VoiceChoice> AvailableVoices { get; }
    public string? SelectedVoiceId => _selectedVoiceId;
    public bool IsSpeaking => _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

    public void SelectVoice(string? voiceId)
    {
        _selectedVoiceId = AvailableVoices.Any(voice => voice.Id == voiceId) ? voiceId : null;
    }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        var spokenText = SpeechTextFormatter.ForSpeech(text);
        if (string.IsNullOrWhiteSpace(spokenText)) return;
        cancellationToken.ThrowIfCancellationRequested();

        using var synthesizer = new SpeechSynthesizer();
        var voice = ResolveVoice();
        if (voice is not null) synthesizer.Voice = SpeechSynthesizer.AllVoices.First(item => item.Id == voice.Id);
        synthesizer.Options.SpeakingRate = 1.04;
        var stream = await synthesizer.SynthesizeTextToStreamAsync(spokenText);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            StopCore();
            _activeStream = stream;
            _player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
            _player.Play();
        }
    }

    public void Stop()
    {
        lock (_sync) StopCore();
    }

    public void Dispose()
    {
        Stop();
        _player.Dispose();
    }

    private VoiceChoice? ResolveVoice()
    {
        var selected = AvailableVoices.FirstOrDefault(voice => voice.Id == _selectedVoiceId);
        if (selected is not null) return selected;

        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return AvailableVoices.FirstOrDefault(voice => voice.IsMale && voice.Language.StartsWith(language, StringComparison.OrdinalIgnoreCase))
            ?? AvailableVoices.FirstOrDefault(voice => voice.IsMale)
            ?? AvailableVoices.FirstOrDefault();
    }

    private void StopCore()
    {
        _player.Pause();
        _player.Source = null;
        ReleaseStream();
    }

    private void ReleaseStream()
    {
        lock (_sync)
        {
            _activeStream?.Dispose();
            _activeStream = null;
        }
    }
}
