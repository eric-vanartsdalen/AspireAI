# Speech Features - Phase 2 Implementation

This document describes the speech-to-text and text-to-speech features implemented in Phase 2 of the AspireAI roadmap.

## Overview

The speech functionality provides:
- **Speech-to-Text (STT)**: Convert spoken words to text using the browser's Web Speech API
- **Text-to-Speech (TTS)**: Convert AI responses and messages to spoken audio using the browser's Speech Synthesis API

## Browser Support

### Speech Recognition (Microphone Input)
- ? Chrome (desktop and mobile)
- ? Edge (desktop)
- ? Safari (macOS and iOS)
- ? Firefox (not supported)

### Text-to-Speech (Audio Output)
- ? Chrome (desktop and mobile)
- ? Edge (desktop)
- ? Safari (macOS and iOS)
- ? Firefox (desktop and mobile)

## Features

### Speech Recognition
- **Continuous listening**: Keeps listening until manually stopped
- **Interim results**: Shows partial transcription in real-time
- **Auto-append**: Recognized speech is automatically added to the text input
- **Visual feedback**: Microphone button changes color and pulses when listening
- **Error handling**: Graceful fallback when speech recognition fails

### Text-to-Speech
- **AI response reading**: Automatically read the latest AI response
- **Individual message reading**: Read any message in the chat history
- **Markdown-to-speech conversion**: Converts markdown formatting to plain text for better speech
- **Visual feedback**: Speaker button changes color and pulses when speaking
- **Voice customization**: Uses browser's default voice settings

## User Interface

### Speech Controls
- **?? Microphone Button**: 
  - Green: Ready to start listening
  - Red (pulsing): Currently listening
  - Gray: Error or not supported
- **?? Speaker Button**: 
  - Blue: Ready to speak
  - Yellow (pulsing): Currently speaking
  - Disabled: No content to speak or not supported

### Status Indicators
- Real-time status text showing current speech activity
- Speech transcript overlay showing recognized text
- Browser support warnings for unsupported features

## Technical Implementation

### Architecture
```
Chat.razor (UI)
    ?
SpeechService.cs (C# Service Layer)
    ?
speech.js (JavaScript Speech Manager)
    ?
Web Speech API (Browser APIs)
```

### Key Components

1. **SpeechService.cs**: C# service providing speech functionality to Blazor components
2. **speech.js**: JavaScript module managing Web Speech API interactions
3. **speech.css**: Styling for speech UI components
4. **Chat.razor**: Updated chat interface with speech controls

### JavaScript API Integration
- **Web Speech API**: Used for speech recognition
- **Speech Synthesis API**: Used for text-to-speech
- **Blazor Interop**: Enables communication between C# and JavaScript

## Usage Instructions

### For Users
1. **Voice Input**:
   - Click the ?? microphone button to start listening
   - Speak your question clearly
   - Click the red button to stop listening
   - Your speech will appear in the text input box
   - Press Enter or click "Send Query" to send your question

2. **Voice Output**:
   - After receiving an AI response, click the ?? speaker button to hear it read aloud
   - Click any ?? button next to individual messages to read them
   - Click the speaker button again to stop playback

### Permissions
- **Microphone Access**: The browser will prompt for microphone permission on first use
- **HTTPS Required**: Speech recognition requires a secure (HTTPS) connection in production

## Configuration Options

The speech functionality supports various configuration options:

- **Language**: Currently set to 'en-US', but can be configured
- **Voice Selection**: Uses browser's default voice, can be customized
- **Speech Rate**: Configurable speaking speed (0.1-10x)
- **Speech Pitch**: Configurable voice pitch (0-2x)
- **Speech Volume**: Configurable volume (0-1)

## Error Handling

The implementation includes comprehensive error handling:
- Browser compatibility detection
- Graceful degradation when features aren't supported
- User-friendly error messages
- Automatic recovery from temporary failures

## Future Enhancements

Potential improvements for future phases:
- Voice command recognition for chat controls
- Multiple language support
- Custom voice selection interface
- Offline speech processing capabilities
- Advanced speech settings panel

## Security Considerations

- Speech data is processed locally in the browser
- No audio data is sent to external servers
- Microphone access is controlled by browser permissions
- HTTPS is required for production deployments

## Troubleshooting

### Common Issues
1. **Microphone not working**: Check browser permissions and ensure HTTPS
2. **Speech not recognized**: Ensure clear speech and supported language
3. **TTS not working**: Check browser support and audio settings
4. **No sound**: Check system volume and browser audio permissions

### Debug Information
Speech status and errors are logged to the browser console for debugging purposes.