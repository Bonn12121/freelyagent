using Windows.Media.SpeechRecognition;
using Windows.Media.Capture;
using Windows.Media;

namespace Freely.Voice;

public sealed class WindowsDictationService : IDictationService
{
    private readonly object _sync = new();
    private SpeechRecognizer? _recognizer;
    private CancellationTokenSource? _activeRecognition;
    private bool _accessGranted;

    public bool IsListening
    {
        get
        {
            lock (_sync) return _activeRecognition is not null;
        }
    }

    public async Task<string> ListenOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!_accessGranted)
        {
            using var microphoneProbe = new MediaCapture();
            await microphoneProbe.InitializeAsync(new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Audio,
                MediaCategory = MediaCategory.Speech,
                AudioProcessing = AudioProcessing.Default
            });
            _accessGranted = true;
        }

        CancellationTokenSource linked;
        SpeechRecognizer recognizer;
        lock (_sync)
        {
            if (_activeRecognition is not null) throw new InvalidOperationException("Dictation is already listening.");
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeRecognition = linked;
            recognizer = new SpeechRecognizer();
            _recognizer = recognizer;
        }

        try
        {
            recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(
                SpeechRecognitionScenario.Dictation, "freely_dictation"));
            recognizer.Timeouts.InitialSilenceTimeout = TimeSpan.FromSeconds(12);
            recognizer.Timeouts.BabbleTimeout = TimeSpan.FromSeconds(45);
            recognizer.Timeouts.EndSilenceTimeout = TimeSpan.FromMilliseconds(850);
            var compilation = await recognizer.CompileConstraintsAsync();
            if (compilation.Status != SpeechRecognitionResultStatus.Success)
            {
                throw new InvalidOperationException($"Windows could not start speech recognition ({compilation.Status}).");
            }

            linked.Token.ThrowIfCancellationRequested();
            var operation = recognizer.RecognizeAsync();
            using var registration = linked.Token.Register(operation.Cancel);
            var result = await operation;
            linked.Token.ThrowIfCancellationRequested();
            return result.Status == SpeechRecognitionResultStatus.Success ? result.Text.Trim() : string.Empty;
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_recognizer, recognizer)) _recognizer = null;
                if (ReferenceEquals(_activeRecognition, linked)) _activeRecognition = null;
            }
            recognizer.Dispose();
            linked.Dispose();
        }
    }

    public void Stop()
    {
        lock (_sync) _activeRecognition?.Cancel();
    }

    public void Dispose() => Stop();
}
