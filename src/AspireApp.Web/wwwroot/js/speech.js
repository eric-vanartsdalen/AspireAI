// Speech functionality for AspireAI
// Implements both Speech-to-Text (microphone input) and Text-to-Speech (audio output)

class SpeechManager {
    constructor() {
        this.recognition = null;
        this.synthesis = window.speechSynthesis;
        this.isListening = false;
        this.isSupported = this.checkBrowserSupport();
        this.dotNetRef = null;
        
        // Speech recognition configuration
        this.recognitionConfig = {
            continuous: true,
            interimResults: true,
            language: 'en-US'
        };
        
        // Text-to-speech configuration
        this.ttsConfig = {
            voice: null,
            rate: 1.0,
            pitch: 1.0,
            volume: 1.0
        };
        
        this.initializeSpeechRecognition();
        this.initializeTextToSpeech();
    }
    
    checkBrowserSupport() {
        const hasSpeechRecognition = 'webkitSpeechRecognition' in window || 'SpeechRecognition' in window;
        const hasSpeechSynthesis = 'speechSynthesis' in window;
        
        console.log('Speech Recognition supported:', hasSpeechRecognition);
        console.log('Speech Synthesis supported:', hasSpeechSynthesis);
        
        return {
            speechRecognition: hasSpeechRecognition,
            textToSpeech: hasSpeechSynthesis,
            both: hasSpeechRecognition && hasSpeechSynthesis
        };
    }
    
    initializeSpeechRecognition() {
        if (!this.isSupported.speechRecognition) {
            console.warn('Speech recognition not supported in this browser');
            return;
        }
        
        const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
        this.recognition = new SpeechRecognition();
        
        // Configure recognition
        this.recognition.continuous = this.recognitionConfig.continuous;
        this.recognition.interimResults = this.recognitionConfig.interimResults;
        this.recognition.lang = this.recognitionConfig.language;
        
        // Event handlers
        this.recognition.onstart = () => {
            console.log('Speech recognition started');
            this.isListening = true;
            this.updateMicrophoneStatus('listening');
        };
        
        this.recognition.onresult = (event) => {
            let interimTranscript = '';
            let finalTranscript = '';
            
            for (let i = event.resultIndex; i < event.results.length; i++) {
                const transcript = event.results[i][0].transcript;
                if (event.results[i].isFinal) {
                    finalTranscript += transcript;
                } else {
                    interimTranscript += transcript;
                }
            }
            
            // Send interim results to Blazor component
            if (this.dotNetRef) {
                this.dotNetRef.invokeMethodAsync('OnSpeechRecognitionResult', finalTranscript, interimTranscript);
            }
        };
        
        this.recognition.onerror = (event) => {
            console.error('Speech recognition error:', event.error);
            this.isListening = false;
            this.updateMicrophoneStatus('error');
            
            if (this.dotNetRef) {
                this.dotNetRef.invokeMethodAsync('OnSpeechRecognitionError', event.error);
            }
        };
        
        this.recognition.onend = () => {
            console.log('Speech recognition ended');
            this.isListening = false;
            this.updateMicrophoneStatus('stopped');
            
            if (this.dotNetRef) {
                this.dotNetRef.invokeMethodAsync('OnSpeechRecognitionEnd');
            }
        };
    }
    
    initializeTextToSpeech() {
        if (!this.isSupported.textToSpeech) {
            console.warn('Text-to-speech not supported in this browser');
            return;
        }
        
        // Load available voices when they're ready
        if (this.synthesis.getVoices().length === 0) {
            this.synthesis.addEventListener('voiceschanged', () => {
                this.loadVoices();
                this.primeSpeechSynthesis();
            });
        } else {
            this.loadVoices();
            this.primeSpeechSynthesis();
        }
    }
    
    primeSpeechSynthesis() {
        // Prime the speech synthesis engine with a silent utterance
        // This prevents the first word from being clipped when actually speaking
        try {
            const primeUtterance = new SpeechSynthesisUtterance(' ');
            primeUtterance.volume = 0; // Silent
            primeUtterance.rate = 10; // Fast
            primeUtterance.pitch = 0; // Low
            this.synthesis.speak(primeUtterance);
            console.log('Speech synthesis primed successfully');
        } catch (error) {
            console.warn('Could not prime speech synthesis:', error);
        }
    }
    
    loadVoices() {
        const voices = this.synthesis.getVoices();
        console.log('Available voices:', voices.length);
        
        // Try to find a good default English voice
        const preferredVoice = voices.find(voice => 
            voice.lang.startsWith('en') && voice.default
        ) || voices.find(voice => 
            voice.lang.startsWith('en')
        ) || voices[0];
        
        this.ttsConfig.voice = preferredVoice;
        console.log('Selected default voice:', preferredVoice?.name);
    }
    
    // Public methods for Blazor integration
    startListening() {
        if (!this.isSupported.speechRecognition) {
            throw new Error('Speech recognition not supported');
        }
        
        if (this.isListening) {
            console.warn('Already listening');
            return false;
        }
        
        try {
            this.recognition.start();
            return true;
        } catch (error) {
            console.error('Error starting speech recognition:', error);
            return false;
        }
    }
    
    stopListening() {
        if (this.recognition && this.isListening) {
            this.recognition.stop();
            return true;
        }
        return false;
    }
    
    speak(text, options = {}) {
        if (!this.isSupported.textToSpeech) {
            throw new Error('Text-to-speech not supported');
        }
        
        if (!text || text.trim() === '') {
            console.warn('No text provided for speech');
            return false;
        }
        
        // Stop any ongoing speech
        this.synthesis.cancel();
        
        // Small delay to ensure synthesis is ready after cancellation
        setTimeout(() => {
            const utterance = new SpeechSynthesisUtterance(text);
            
            // Apply configuration
            utterance.voice = options.voice || this.ttsConfig.voice;
            utterance.rate = options.rate || this.ttsConfig.rate;
            utterance.pitch = options.pitch || this.ttsConfig.pitch;
            utterance.volume = options.volume || this.ttsConfig.volume;
            
            // Event handlers
            utterance.onstart = () => {
                console.log('Speech synthesis started');
                if (this.dotNetRef) {
                    this.dotNetRef.invokeMethodAsync('OnTextToSpeechStart');
                }
            };
            
            utterance.onend = () => {
                console.log('Speech synthesis ended');
                if (this.dotNetRef) {
                    this.dotNetRef.invokeMethodAsync('OnTextToSpeechEnd');
                }
            };
        
            utterance.onerror = (event) => {
                console.error('Speech synthesis error:', event.error);
                if (this.dotNetRef) {
                    this.dotNetRef.invokeMethodAsync('OnTextToSpeechError', event.error);
                }
            };
    
            this.synthesis.speak(utterance);
        }, 500); // ms delay to ensure engine readiness
        
        return true;
    }
    
    stopSpeaking() {
        if (this.synthesis.speaking) {
            this.synthesis.cancel();
            return true;
        }
        return false;
    }
    
    // Configuration methods
    setLanguage(language) {
        this.recognitionConfig.language = language;
        if (this.recognition) {
            this.recognition.lang = language;
        }
    }
    
    setSpeechRate(rate) {
        this.ttsConfig.rate = Math.max(0.1, Math.min(10, rate));
    }
    
    setSpeechPitch(pitch) {
        this.ttsConfig.pitch = Math.max(0, Math.min(2, pitch));
    }
    
    setSpeechVolume(volume) {
        this.ttsConfig.volume = Math.max(0, Math.min(1, volume));
    }
    
    getAvailableVoices() {
        return this.synthesis.getVoices().map(voice => ({
            name: voice.name,
            lang: voice.lang,
            default: voice.default,
            localService: voice.localService
        }));
    }
    
    setVoice(voiceName) {
        const voices = this.synthesis.getVoices();
        const selectedVoice = voices.find(voice => voice.name === voiceName);
        if (selectedVoice) {
            this.ttsConfig.voice = selectedVoice;
            return true;
        }
        return false;
    }
    
    // Status and support methods
    isSpeechRecognitionSupported() {
        return this.isSupported.speechRecognition;
    }
    
    isTextToSpeechSupported() {
        return this.isSupported.textToSpeech;
    }
    
    isCurrentlyListening() {
        return this.isListening;
    }
    
    isCurrentlySpeaking() {
        return this.synthesis.speaking;
    }
    
    updateMicrophoneStatus(status) {
        // Update UI elements to reflect microphone status
        const micButton = document.getElementById('mic-button');
        if (micButton) {
            micButton.setAttribute('data-status', status);
            
            switch (status) {
                case 'listening':
                    micButton.classList.add('listening');
                    micButton.classList.remove('error');
                    break;
                case 'stopped':
                    micButton.classList.remove('listening', 'error');
                    break;
                case 'error':
                    micButton.classList.add('error');
                    micButton.classList.remove('listening');
                    break;
            }
        }
    }
    
    // Initialize with .NET reference
    initialize(dotNetReference) {
        this.dotNetRef = dotNetReference;
        console.log('Speech manager initialized with .NET reference');
    }
    
    // Cleanup
    dispose() {
        if (this.recognition) {
            this.recognition.abort();
        }
        if (this.synthesis.speaking) {
            this.synthesis.cancel();
        }
        this.dotNetRef = null;
        console.log('Speech manager disposed');
    }
}

// Global speech manager instance
let speechManager = null;

// Global functions for Blazor interop
window.initializeSpeechManager = function(dotNetReference) {
    if (!speechManager) {
        speechManager = new SpeechManager();
    }
    speechManager.initialize(dotNetReference);
    return speechManager.isSupported;
};

window.startSpeechRecognition = function() {
    return speechManager ? speechManager.startListening() : false;
};

window.stopSpeechRecognition = function() {
    return speechManager ? speechManager.stopListening() : false;
};

window.speakText = function(text, options = {}) {
    return speechManager ? speechManager.speak(text, options) : false;
};

window.stopTextToSpeech = function() {
    return speechManager ? speechManager.stopSpeaking() : false;
};

window.getSpeechSupport = function() {
    return speechManager ? speechManager.isSupported : { speechRecognition: false, textToSpeech: false, both: false };
};

window.getSpeechStatus = function() {
    if (!speechManager) return { listening: false, speaking: false };
    return {
        listening: speechManager.isCurrentlyListening(),
        speaking: speechManager.isCurrentlySpeaking()
    };
};

window.configureSpeech = function(config) {
    if (!speechManager) return false;
    
    if (config.language) speechManager.setLanguage(config.language);
    if (config.rate) speechManager.setSpeechRate(config.rate);
    if (config.pitch) speechManager.setSpeechPitch(config.pitch);
    if (config.volume) speechManager.setSpeechVolume(config.volume);
    if (config.voice) speechManager.setVoice(config.voice);
    
    return true;
};

window.getAvailableVoices = function() {
    return speechManager ? speechManager.getAvailableVoices() : [];
};

window.disposeSpeechManager = function() {
    if (speechManager) {
        speechManager.dispose();
        speechManager = null;
    }
};