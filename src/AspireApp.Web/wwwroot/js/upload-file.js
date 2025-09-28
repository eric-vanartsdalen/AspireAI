// File upload JavaScript for AspireAI
console.log('upload-file.js loading...');

// Diagnostic function to test JavaScript availability
window.testUploadFunctions = function() {
    const results = {
        uploadFileJS: typeof window.uploadFile === 'function',
        initializeFileUploadJS: typeof window.initializeFileUpload === 'function',
        formatFileSizeJS: typeof window.formatFileSize === 'function',
        validateFileJS: typeof window.validateFile === 'function',
        fileInput: document.getElementById('fileInput') !== null,
        timestamp: new Date().toISOString()
    };
    console.log('JavaScript Upload Functions Test Results:', results);
    return results;
};

window.initializeFileUpload = function (inputId, dotNetRef) {
    console.log('initializeFileUpload called with inputId:', inputId);
    const input = document.getElementById(inputId);
    if (input) {
        console.log('File input element found, adding event listener');
        input.addEventListener('change', function () {
            console.log('File input changed, files selected:', input.files.length);
            if (input.files.length > 0) {
                const file = input.files[0];
                console.log('Notifying Blazor of file selection:', file.name);
                dotNetRef.invokeMethodAsync('HandleFileSelected', {
                    name: file.name,
                    size: file.size,
                    contentType: file.type,
                    lastModified: file.lastModified
                });
            }
        });
        return true;
    } else {
        console.error('File input element not found with id:', inputId);
        return false;
    }
};

window.uploadFile = async function (inputId, uploadUrl) {
    console.log('uploadFile called with inputId:', inputId, 'uploadUrl:', uploadUrl);
    const input = document.getElementById(inputId);
    if (!input || input.files.length === 0) {
        console.error('No file selected or input not found');
        return { success: false, error: "No file selected." };
    }
    
    const file = input.files[0];
    console.log('Uploading file:', file.name, 'size:', file.size);
    const formData = new FormData();
    formData.append('file', file);

    try {
        console.log('Sending file to server...');
        const response = await fetch(uploadUrl, {
            method: 'POST',
            body: formData,
            headers: {
                // Don't set Content-Type header - browser will set it with boundary for FormData
                'X-Requested-With': 'XMLHttpRequest'
            }
        });
        
        console.log('Server response status:', response.status);
        
        if (response.ok) {
            const result = await response.json();
            console.log('Upload result:', result);
            if (result.success) {
                return { 
                    success: true, 
                    fileName: result.fileName || result.originalFileName,
                    size: result.length 
                };
            } else {
                return { 
                    success: false, 
                    error: result.error || "Upload failed with unknown error" 
                };
            }
        } else {
            // Try to parse error response as JSON, fallback to text
            let errorMessage;
            try {
                const errorResult = await response.json();
                errorMessage = errorResult.error || errorResult.message || `Server error: ${response.status}`;
                console.error('Upload error (JSON):', errorResult);
            } catch {
                errorMessage = await response.text() || `Server error: ${response.status}`;
                console.error('Upload error (Text):', errorMessage);
            }
            return { success: false, error: errorMessage };
        }
    } catch (e) {
        console.error('Upload error:', e);
        return { success: false, error: `Network error: ${e.message}` };
    }
};

// Helper function to format file sizes
window.formatFileSize = function(bytes) {
    if (bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
};

// Function to validate file before upload
window.validateFile = function(file, maxSize = 10485760, allowedExtensions = ['.pdf', '.docx', '.txt', '.md']) {
    const errors = [];
    
    if (!file) {
        errors.push('No file selected');
        return { valid: false, errors };
    }
    
    // Check file size
    if (file.size > maxSize) {
        errors.push(`File size (${window.formatFileSize(file.size)}) exceeds maximum allowed size (${window.formatFileSize(maxSize)})`);
    }
    
    // Check file extension
    const extension = '.' + file.name.split('.').pop().toLowerCase();
    if (allowedExtensions.length > 0 && !allowedExtensions.includes(extension)) {
        errors.push(`File type '${extension}' is not allowed. Allowed types: ${allowedExtensions.join(', ')}`);
    }
    
    return {
        valid: errors.length === 0,
        errors: errors
    };
};

// Initialize when DOM is ready
document.addEventListener('DOMContentLoaded', function() {
    console.log('DOM Content Loaded - upload-file.js functions available');
    window.testUploadFunctions();
});

console.log('upload-file.js loaded successfully');
console.log('Available functions:', {
    initializeFileUpload: typeof window.initializeFileUpload,
    uploadFile: typeof window.uploadFile,
    formatFileSize: typeof window.formatFileSize,
    validateFile: typeof window.validateFile,
    testUploadFunctions: typeof window.testUploadFunctions
});