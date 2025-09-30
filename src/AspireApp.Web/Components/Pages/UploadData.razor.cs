using AspireApp.Web.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Text.Json;

namespace AspireApp.Web.Components.Pages;

public partial class UploadData : ComponentBase, IAsyncDisposable, IDisposable
{
    private string? UploadMessage;
    private string MessageClass = "";
    private string? _selectedFileName;
    private DotNetObjectReference<UploadData>? _objectReference;
    private List<FileMetadata>? _uploadedFiles;
    private bool _isLoading = true;
    private bool _isUploading = false;
    private int _uploadProgress = 0;
    private List<string> UploadErrors = new();
    
    // Duplicate detection tracking
    private bool _isDuplicateDetected = false;
    private DuplicateFileInfo? _duplicateFileInfo = null;
    private bool _showDuplicateToast = false;

    [Inject]
    public IConfiguration Configuration { get; set; } = default!;

    private long MaxFileSize => Configuration.GetValue<long?>("FileUpload:MaxFileSize") ?? 10485760; // 10MB default

    protected override async Task OnInitializedAsync()
    {
        Logger.LogInformation("UploadData component initialized");
        await LoadUploadedFiles();
    }

    private async Task LoadUploadedFiles()
    {
        _isLoading = true;
        try
        {
            // Ensure the file storage service is properly initialized
            var initialized = await FileStorageService.EnsureInitializedAsync();
            if (!initialized)
            {
                UploadMessage = "Database initialization failed. Please check the application logs.";
                MessageClass = "error";
                _uploadedFiles = new List<FileMetadata>();
                return;
            }

            _uploadedFiles = await FileStorageService.GetAllFilesAsync();
            Logger.LogInformation("Loaded {Count} uploaded files", _uploadedFiles.Count);
            
            // Clear any previous error messages if loading succeeds
            if (_uploadedFiles != null)
            {
                UploadMessage = string.Empty;
                MessageClass = "";
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading uploaded files");
            UploadMessage = $"Error loading files: {ex.Message}. The database may need to be initialized.";
            MessageClass = "error";
            _uploadedFiles = new List<FileMetadata>();
        }
        finally
        {
            _isLoading = false;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            Logger.LogInformation("UploadData component rendered for the first time");
            _objectReference = DotNetObjectReference.Create(this);
            
            // Wait for JavaScript to load and try multiple times
            await InitializeJavaScriptWithRetry();
        }
    }

    private async Task InitializeJavaScriptWithRetry()
    {
        const int maxRetries = 10;
        const int delayMs = 500;
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Check if the JavaScript function exists
                var functionExists = await JSRuntime.InvokeAsync<bool>("eval", 
                    "typeof window.initializeFileUpload === 'function'");
                
                if (functionExists)
                {
                    await JSRuntime.InvokeVoidAsync("initializeFileUpload", "fileInput", _objectReference);
                    Logger.LogInformation("File upload JavaScript initialized successfully on attempt {Attempt}", attempt);
                    return;
                }
                else
                {
                    Logger.LogInformation("JavaScript function not yet available, attempt {Attempt}/{MaxRetries}", attempt, maxRetries);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "JavaScript initialization attempt {Attempt}/{MaxRetries} failed", attempt, maxRetries);
            }
            
            if (attempt < maxRetries)
            {
                await Task.Delay(delayMs);
            }
        }
        
        Logger.LogError("Failed to initialize JavaScript after {MaxRetries} attempts", maxRetries);
        UploadMessage = "JavaScript initialization failed. Please refresh the page.";
        MessageClass = "warning";
        StateHasChanged();
    }

    private async Task UploadFile()
    {
        if (_isUploading) return;

        try
        {
            _isUploading = true;
            _uploadProgress = 0;
            UploadMessage = "Starting upload...";
            MessageClass = "info";
            UploadErrors.Clear();
            
            // Clear previous duplicate detection state
            _isDuplicateDetected = false;
            _duplicateFileInfo = null;
            _showDuplicateToast = false;
            
            StateHasChanged();

            // Check if JavaScript functions are available
            var uploadFunctionExists = await JSRuntime.InvokeAsync<bool>("eval", 
                "typeof window.uploadFile === 'function'");
            
            if (!uploadFunctionExists)
            {
                throw new InvalidOperationException("JavaScript upload function not available");
            }

            // Simulate progress for user feedback
            for (int i = 10; i <= 30; i += 10)
            {
                _uploadProgress = i;
                StateHasChanged();
                await Task.Delay(100);
            }

            var result = await JSRuntime.InvokeAsync<FileUploadResult>("uploadFile", "fileInput", "/api/FileUpload");

            _uploadProgress = 90;
            StateHasChanged();

            Logger.LogInformation("Upload result received: Success={Success}, IsDuplicate={IsDuplicate}, FileName={FileName}", 
                result.Success, result.IsDuplicate, result.FileName);

            if (result.Success)
            {
                _uploadProgress = 100;
                
                // Handle duplicate detection
                if (result.IsDuplicate)
                {
                    Logger.LogInformation("Duplicate detected - showing toast notification");
                    _isDuplicateDetected = true;
                    _showDuplicateToast = true;
                    _duplicateFileInfo = new DuplicateFileInfo
                    {
                        FileName = result.ExistingFileName ?? "Unknown",
                        Size = result.Size,
                        UploadedAt = result.ExistingUploadedAt ?? DateTime.Now,
                        FileHash = result.FileHash ?? "Unknown"
                    };
                    
                    UploadMessage = $"This file is identical to '{result.ExistingFileName}' and was not uploaded to prevent duplicates.";
                    MessageClass = "warning";
                    
                    Logger.LogInformation("Duplicate file detected: {FileName}, Existing: {ExistingFile}, Hash: {Hash}", 
                        result.FileName, result.ExistingFileName, result.FileHash);
                        
                    Logger.LogInformation("Toast state: _showDuplicateToast={ShowToast}, _isDuplicateDetected={IsDuplicate}", 
                        _showDuplicateToast, _isDuplicateDetected);
                        
                    // Auto-hide toast after 8 seconds
                    _ = Task.Delay(8000).ContinueWith(_ => 
                    {
                        InvokeAsync(() =>
                        {
                            _showDuplicateToast = false;
                            StateHasChanged();
                        });
                    });
                }
                else
                {
                    Logger.LogInformation("File uploaded successfully - not a duplicate");
                    UploadMessage = result.Message ?? $"File '{result.FileName}' uploaded successfully.";
                    MessageClass = "success";
                    Logger.LogInformation("File uploaded successfully: {FileName}, Size: {Size}, Hash: {Hash}", 
                        result.FileName, result.Size, result.FileHash);
                }

                // Clear the file input
                await JSRuntime.InvokeVoidAsync("eval", "document.getElementById('fileInput').value = ''");
                _selectedFileName = null;

                // Reload the file list only if it wasn't a duplicate
                if (!result.IsDuplicate)
                {
                    await LoadUploadedFiles();
                }
            }
            else
            {
                _uploadProgress = 0;
                UploadMessage = "Upload Failed.";
                MessageClass = "error";
                UploadErrors.Clear();
                Logger.LogError("Upload failed: {Error}", result.Error);

                if (!string.IsNullOrWhiteSpace(result.Error))
                {
                    try
                    {
                        // Try to parse error as JSON first
                        using var doc = JsonDocument.Parse(result.Error);
                        if (doc.RootElement.TryGetProperty("error", out var errorProp))
                        {
                            UploadErrors.Add(errorProp.GetString() ?? "Unknown error");
                        }
                        else if (doc.RootElement.TryGetProperty("errors", out var errorsProp))
                        {
                            foreach (var error in errorsProp.EnumerateObject())
                            {
                                foreach (var msg in error.Value.EnumerateArray())
                                {
                                    UploadErrors.Add(msg.GetString() ?? "Unknown error");
                                }
                            }
                        }
                        else
                        {
                            UploadErrors.Add(result.Error);
                        }
                    }
                    catch (JsonException)
                    {
                        // Not JSON, just show the error string
                        UploadErrors.Add(result.Error);
                    }
                }
            }
        }
        catch (JSException ex) when (ex.Message.Contains("uploadFile") || ex.Message.Contains("not a function"))
        {
            Logger.LogError(ex, "JavaScript file upload function not available");
            UploadMessage = "File upload functionality not available. Please refresh the page and try again.";
            MessageClass = "error";
            UploadErrors.Clear();
            UploadErrors.Add("JavaScript file upload function not found. Try refreshing the page.");
        }
        catch (InvalidOperationException ex)
        {
            Logger.LogError(ex, "Upload function validation failed");
            UploadMessage = "File upload functionality not ready. Please refresh the page and try again.";
            MessageClass = "error";
            UploadErrors.Clear();
            UploadErrors.Add(ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in UploadFile");
            UploadMessage = $"Error: {ex.Message}";
            MessageClass = "error";
            UploadErrors.Clear();
            UploadErrors.Add(ex.Message);
        }
        finally
        {
            _isUploading = false;
            if (_uploadProgress != 100)
            {
                _uploadProgress = 0;
            }
            StateHasChanged();

            // Clear progress after success message is shown
            if (_uploadProgress == 100)
            {
                await Task.Delay(2000);
                _uploadProgress = 0;
                StateHasChanged();
            }
        }
    }

    private async Task DeleteFile(int id)
    {
        try
        {
            var success = await FileStorageService.DeleteFileAsync(id);
            if (success)
            {
                UploadMessage = "File deleted successfully.";
                MessageClass = "success";
                
                // Clear duplicate detection state when showing other messages
                _isDuplicateDetected = false;
                _duplicateFileInfo = null;
                _showDuplicateToast = false;
                
                await LoadUploadedFiles();
            }
            else
            {
                UploadMessage = "Failed to delete file. File may not exist.";
                MessageClass = "error";
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting file");
            UploadMessage = $"Error deleting file: {ex.Message}";
            MessageClass = "error";
        }
    }

    [JSInvokable]
    public void HandleFileSelected(FileInfo fileInfo)
    {
        Logger.LogInformation("File selected: {FileName}, Size: {Size}", fileInfo.Name, fileInfo.Size);
        _selectedFileName = fileInfo.Name;
        
        // Clear any previous messages when a new file is selected
        UploadMessage = string.Empty;
        MessageClass = "";
        UploadErrors.Clear();
        _isDuplicateDetected = false;
        _duplicateFileInfo = null;
        _showDuplicateToast = false;

        StateHasChanged();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _objectReference?.Dispose();
            Logger.LogInformation("UploadData component disposing");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error during component disposal");
        }
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        try
        {
            _objectReference?.Dispose();
            Logger.LogInformation("UploadData component disposing");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error during component disposal");
        }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size = size / 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }

    private void CloseDuplicateToast()
    {
        _showDuplicateToast = false;
        StateHasChanged();
    }

    private void TestDuplicateToast()
    {
        Logger.LogInformation("Test duplicate toast button clicked");
        _isDuplicateDetected = true;
        _showDuplicateToast = true;
        _duplicateFileInfo = new DuplicateFileInfo
        {
            FileName = "test-file.pdf",
            Size = 1024000,
            UploadedAt = DateTime.Now.AddDays(-1),
            FileHash = "ABC123DEF456..."
        };
        
        UploadMessage = "Test duplicate message";
        MessageClass = "warning";
        
        Logger.LogInformation("Test toast state set: _showDuplicateToast={ShowToast}", _showDuplicateToast);
        StateHasChanged();
        
        // Auto-hide after 8 seconds
        _ = Task.Delay(8000).ContinueWith(_ => 
        {
            InvokeAsync(() =>
            {
                _showDuplicateToast = false;
                StateHasChanged();
            });
        });
    }

    public class FileInfo
    {
        public string Name { get; set; } = string.Empty;
        public long Size { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public long LastModified { get; set; }
    }

    public class FileUploadResult
    {
        public bool Success { get; set; }
        public string? FileName { get; set; }
        public long Size { get; set; }
        public string? Error { get; set; }
        public bool IsDuplicate { get; set; }
        public string? Message { get; set; }
        public string? FileHash { get; set; }
        public string? ExistingFileName { get; set; }
        public int? ExistingFileId { get; set; }
        public DateTime? ExistingUploadedAt { get; set; }
    }

    public class DuplicateFileInfo
    {
        public string FileName { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime UploadedAt { get; set; }
        public string FileHash { get; set; } = string.Empty;
    }
}
