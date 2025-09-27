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
- **Visual feedback**: Record button changes to stop button when listening with pulsing animation
- **Error handling**: Graceful fallback when speech recognition fails

### Text-to-Speech
- **AI response reading**: Automatically read the latest AI response
- **Individual message reading**: Read any message in the chat history
- **Markdown-to-speech conversion**: Converts markdown formatting to plain text for better speech
- **Visual feedback**: Playback buttons change to stop buttons when speaking with pulsing animation
- **Voice customization**: Uses browser's default voice settings

## User Interface

### Speech Controls
- **?? Record Button (record.png)**: 
  - Green circular button with record icon: Ready to start listening
  - Red stop button (stop.png) with pulsing animation: Currently listening
  - Gray: Error or not supported

- **?? Playback Buttons (playback.png)**: 
  - Blue circular button with speaker icon: Ready to speak
  - Red stop button (stop.png) with pulsing animation: Currently speaking
  - Disabled (grayed out): No content to speak or not supported

### Button Images
The interface uses three PNG images located in `wwwroot/images/`:
- **record.png**: Microphone/record icon for speech input
- **playback.png**: Speaker icon for text-to-speech output
- **stop.png**: Stop icon used for both stopping recording and stopping playback

### Status Indicators
- Real-time status text showing current speech activity
- Speech transcript overlay showing recognized text
- Browser support warnings for unsupported features
- Individual message playback buttons in chat history

## Usage Instructions

### For Users
1. **Voice Input**:
   - Click the record button (record.png) to start listening
   - The button changes to a red stop button (stop.png) with pulsing animation
   - Speak your question clearly
   - Click the stop button to finish recording
   - Your speech will appear in the text input box
   - Press Enter or click "Send Query" to send your question

2. **Voice Output**:
   - After receiving an AI response, click the playback button next to the main response
   - Click any playback button next to individual messages to read them
   - The button changes to a stop button while speaking
   - Click the stop button to halt playback

### Permissions
- **Microphone Access**: The browser will prompt for microphone permission on first use
- **HTTPS Required**: Speech recognition requires a secure (HTTPS) connection in production

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
3. **speech.css**: Styling for speech UI components with PNG button support
4. **Chat.razor**: Updated chat interface with image-based speech controls
5. **PNG Images**: record.png, playback.png, stop.png for button states

### Button State Management
- **Microphone Button**: Switches between record.png and stop.png based on listening state
- **TTS Buttons**: Switch between playback.png and stop.png based on speaking state
- **Per-Message Tracking**: Each message can be individually played/stopped
- **Mutual Exclusion**: Starting speech recognition stops TTS and vice versa

### JavaScript API Integration
- **Web Speech API**: Used for speech recognition
- **Speech Synthesis API**: Used for text-to-speech
- **Blazor Interop**: Enables communication between C# and JavaScript

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
- Per-message error isolation (one failing message doesn't affect others)

## UI/UX Enhancements

### Visual Design
- **Consistent Icons**: All speech functions use clear, recognizable PNG icons
- **State Feedback**: Button images change to reflect current operation
- **Animations**: Pulsing effects during active speech operations
- **Accessibility**: Alt text and proper ARIA labels for screen readers
- **Responsive Design**: Buttons scale appropriately on mobile devices

### User Experience
- **Intuitive Controls**: Record and playback metaphors are universally understood
- **Immediate Feedback**: Users can see at a glance what the system is doing
- **Non-Destructive Actions**: Speech recognition adds to existing text rather than replacing it
- **Contextual Availability**: TTS buttons only appear where relevant (Assistant messages)

## Future Enhancements

Potential improvements for future phases:
- Voice command recognition for chat controls
- Multiple language support with language detection
- Custom voice selection interface
- Offline speech processing capabilities
- Advanced speech settings panel
- Custom button themes and icon packs

## Security Considerations

- Speech data is processed locally in the browser
- No audio data is sent to external servers
- Microphone access is controlled by browser permissions
- HTTPS is required for production deployments
- PNG images are served as static assets (no dynamic generation)

## Troubleshooting

### Common Issues
1. **Microphone not working**: Check browser permissions and ensure HTTPS
2. **Speech not recognized**: Ensure clear speech and supported language
3. **TTS not working**: Check browser support and audio settings
4. **No sound**: Check system volume and browser audio permissions
5. **Images not loading**: Verify PNG files are in `wwwroot/images/` directory

### Debug Information
Speech status and errors are logged to the browser console for debugging purposes.

### Asset Requirements
Ensure the following PNG images are present in `src/AspireApp.Web/wwwroot/images/`:
- `record.png` - Microphone/record icon
- `playback.png` - Speaker/play icon  
- `stop.png` - Stop icon (used for both record and playback stop states)