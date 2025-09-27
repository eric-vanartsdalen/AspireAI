// Store the .NET reference
let dotNetRef = null;

// Handle Ctrl+C keyboard shortcut
function handleKeyDown(e) {
    if (e.ctrlKey && e.key === 'c' && dotNetRef) {
        e.preventDefault();
        dotNetRef.invokeMethodAsync('HandleCtrlC');
    }
}

// Initialize keyboard shortcuts
window.initializeKeyboardShortcuts = function (instance) {
    dotNetRef = instance;
    document.addEventListener('keydown', handleKeyDown);
};

// Focus an element
window.focusElement = function (element) {
    if (element?.focus instanceof Function) {
        element.focus();
    }
};

// Scroll to bottom of page or specific chat container
window.scrollToBottom = function (containerId = null) {
    if (containerId) {
        const container = document.getElementById(containerId);
        if (container) {
            container.scrollTo({ top: container.scrollHeight, behavior: 'smooth' });
            return;
        }
    }
    
    // Fallback: scroll the entire window
    window.scrollTo({ top: document.documentElement.scrollHeight, behavior: 'smooth' });
};

// Scroll chat messages container to bottom
window.scrollChatToBottom = function () {
    const chatContainer = document.getElementById('chat-messages-container');
    if (chatContainer) {
        chatContainer.scrollTo({ top: chatContainer.scrollHeight, behavior: 'smooth' });
    } else {
        // Fallback to scrolling the entire window
        window.scrollTo({ top: document.documentElement.scrollHeight, behavior: 'smooth' });
    }
};

// Check if user has scrolled up in chat container
window.isUserScrolledUpInChat = function () {
    const chatContainer = document.getElementById('chat-messages-container');
    if (chatContainer) {
        const threshold = 100; // pixels from bottom
        return (chatContainer.scrollHeight - chatContainer.scrollTop - chatContainer.clientHeight) > threshold;
    }
    return false;
};

// Cleanup
window.dispose = function () {
    if (dotNetRef) {
        document.removeEventListener('keydown', handleKeyDown);
        dotNetRef = null;
    }
};
