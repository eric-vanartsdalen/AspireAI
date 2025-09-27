using Microsoft.JSInterop;

namespace AspireApp.Web.Components.Shared
{
    public class SpeechService : IAsyncDisposable
    {
        private readonly IJSRuntime _jsRuntime;
        private DotNetObjectReference<SpeechService>? _dotNetRef;
        private bool _isInitialized = false;

        public SpeechService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        // Events for speech recognition
        public event Action<string, string>? SpeechRecognitionResult;
        public event Action<string>? SpeechRecognitionError;
        public event Action? SpeechRecognitionEnd;

        // Events for text-to-speech
        public event Action? TextToSpeechStart;
        public event Action? TextToSpeechEnd;
        public event Action<string>? TextToSpeechError;

        public async Task<SpeechSupport> InitializeAsync()
        {
            if (_isInitialized) return await GetSpeechSupportAsync();

            try
            {
                _dotNetRef?.Dispose();
                _dotNetRef = DotNetObjectReference.Create(this);

                var support = await _jsRuntime.InvokeAsync<SpeechSupport>("initializeSpeechManager", _dotNetRef);
                _isInitialized = true;
                
                Console.WriteLine($"Speech service initialized - Recognition: {support.SpeechRecognition}, TTS: {support.TextToSpeech}");
                return support;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing speech service: {ex.Message}");
                return new SpeechSupport { SpeechRecognition = false, TextToSpeech = false, Both = false };
            }
        }

        public async Task<bool> StartListeningAsync()
        {
            if (!_isInitialized) await InitializeAsync();

            try
            {
                return await _jsRuntime.InvokeAsync<bool>("startSpeechRecognition");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting speech recognition: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> StopListeningAsync()
        {
            if (!_isInitialized) return false;

            try
            {
                return await _jsRuntime.InvokeAsync<bool>("stopSpeechRecognition");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping speech recognition: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SpeakAsync(string text, SpeechOptions? options = null)
        {
            if (!_isInitialized) await InitializeAsync();

            if (string.IsNullOrWhiteSpace(text))
            {
                Console.WriteLine("No text provided for speech");
                return false;
            }

            try
            {
                return await _jsRuntime.InvokeAsync<bool>("speakText", text, options ?? new SpeechOptions());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error speaking text: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> StopSpeakingAsync()
        {
            if (!_isInitialized) return false;

            try
            {
                return await _jsRuntime.InvokeAsync<bool>("stopTextToSpeech");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping text-to-speech: {ex.Message}");
                return false;
            }
        }

        public async Task<SpeechSupport> GetSpeechSupportAsync()
        {
            try
            {
                return await _jsRuntime.InvokeAsync<SpeechSupport>("getSpeechSupport");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting speech support: {ex.Message}");
                return new SpeechSupport { SpeechRecognition = false, TextToSpeech = false, Both = false };
            }
        }

        public async Task<SpeechStatus> GetSpeechStatusAsync()
        {
            if (!_isInitialized) return new SpeechStatus { Listening = false, Speaking = false };

            try
            {
                return await _jsRuntime.InvokeAsync<SpeechStatus>("getSpeechStatus");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting speech status: {ex.Message}");
                return new SpeechStatus { Listening = false, Speaking = false };
            }
        }

        public async Task<bool> ConfigureSpeechAsync(SpeechConfiguration config)
        {
            if (!_isInitialized) await InitializeAsync();

            try
            {
                return await _jsRuntime.InvokeAsync<bool>("configureSpeech", config);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error configuring speech: {ex.Message}");
                return false;
            }
        }

        public async Task<VoiceInfo[]> GetAvailableVoicesAsync()
        {
            if (!_isInitialized) await InitializeAsync();

            try
            {
                return await _jsRuntime.InvokeAsync<VoiceInfo[]>("getAvailableVoices") ?? Array.Empty<VoiceInfo>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting available voices: {ex.Message}");
                return Array.Empty<VoiceInfo>();
            }
        }

        // JavaScript callback methods
        [JSInvokable]
        public void OnSpeechRecognitionResult(string finalTranscript, string interimTranscript)
        {
            Console.WriteLine($"Speech recognition result - Final: '{finalTranscript}', Interim: '{interimTranscript}'");
            SpeechRecognitionResult?.Invoke(finalTranscript, interimTranscript);
        }

        [JSInvokable]
        public void OnSpeechRecognitionError(string error)
        {
            Console.WriteLine($"Speech recognition error: {error}");
            SpeechRecognitionError?.Invoke(error);
        }

        [JSInvokable]
        public void OnSpeechRecognitionEnd()
        {
            Console.WriteLine("Speech recognition ended");
            SpeechRecognitionEnd?.Invoke();
        }

        [JSInvokable]
        public void OnTextToSpeechStart()
        {
            Console.WriteLine("Text-to-speech started");
            TextToSpeechStart?.Invoke();
        }

        [JSInvokable]
        public void OnTextToSpeechEnd()
        {
            Console.WriteLine("Text-to-speech ended");
            TextToSpeechEnd?.Invoke();
        }

        [JSInvokable]
        public void OnTextToSpeechError(string error)
        {
            Console.WriteLine($"Text-to-speech error: {error}");
            TextToSpeechError?.Invoke(error);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_isInitialized)
                {
                    await _jsRuntime.InvokeVoidAsync("disposeSpeechManager");
                }
            }
            catch (JSDisconnectedException)
            {
                // Ignore disconnect exceptions during disposal
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error disposing speech service: {ex.Message}");
            }
            finally
            {
                _dotNetRef?.Dispose();
                _dotNetRef = null;
                _isInitialized = false;
            }
        }
    }

    // Supporting classes for speech functionality
    public class SpeechSupport
    {
        public bool SpeechRecognition { get; set; }
        public bool TextToSpeech { get; set; }
        public bool Both { get; set; }
    }

    public class SpeechStatus
    {
        public bool Listening { get; set; }
        public bool Speaking { get; set; }
    }

    public class SpeechOptions
    {
        public string? Voice { get; set; }
        public double Rate { get; set; } = 1.0;
        public double Pitch { get; set; } = 1.0;
        public double Volume { get; set; } = 1.0;
    }

    public class SpeechConfiguration
    {
        public string? Language { get; set; }
        public double? Rate { get; set; }
        public double? Pitch { get; set; }
        public double? Volume { get; set; }
        public string? Voice { get; set; }
    }

    public class VoiceInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Lang { get; set; } = string.Empty;
        public bool Default { get; set; }
        public bool LocalService { get; set; }
    }
}