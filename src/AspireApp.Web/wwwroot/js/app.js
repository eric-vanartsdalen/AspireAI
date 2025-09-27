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

// Scroll to bottom of page
window.scrollToBottom = function () {
    window.scrollTo({ top: document.documentElement.scrollHeight, behavior: 'smooth' });
};

// Cleanup
window.dispose = function () {
    if (dotNetRef) {
        document.removeEventListener('keydown', handleKeyDown);
        dotNetRef = null;
    }
};
