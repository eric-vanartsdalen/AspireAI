using AspireApp.Web.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Text.Json;

namespace AspireApp.Web.Components.Pages;

public partial class UploadData : ComponentBase, IAsyncDisposable, IDisposable
{
    private string? _uploadMessage;
    private string _messageClass = "";
    private IBrowserFile? _selectedBrowserFile;
    private DotNetObjectReference<UploadData>? _objectReference;
    protected List<FileMetadata>? Files;
    private bool _isLoading = true;
    private bool _isUploading = false;
    private int _uploadProgress = 0;
    private List<string> _uploadErrors = new();
    
    // Duplicate detection tracking
    protected bool _isDuplicate;
    protected DuplicateFileInfo? _duplicateFileInfo;
    protected bool _showDuplicateToast;

    // URL upload properties
    private bool _isUrlMode;
    private string _fileUrl = string.Empty;

    [Inject]
    public IConfiguration Configuration { get; set; } = default!;

    [Inject]
    public IHttpClientFactory ClientFactory { get; set; } = default!;

    [Inject]
    public ILogger<UploadData> Logger { get; set; } = default!;

    [Inject]
    public IJSRuntime JSRuntime { get; set; } = default!;

    [Inject]
    public FileStorageService FileStorageService { get; set; } = default!;

    private long MaxFileSize => Configuration.GetValue<long?>("FileUpload:MaxFileSize") ?? 10485760; // 10MB default

    protected bool IsFileSelected => _selectedBrowserFile != null;

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
                _uploadMessage = "Database initialization failed. Please check the application logs.";
                _messageClass = "error";
                Files = new List<FileMetadata>();
                return;
            }

            Files = await FileStorageService.GetAllFilesAsync();
            Logger.LogInformation("Loaded {Count} uploaded files", Files.Count);
            
            // Clear any previous error messages if loading succeeds
            if (Files != null)
            {
                _uploadMessage = string.Empty;
                _messageClass = "";
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading uploaded files");
            _uploadMessage = $"Error loading files: {ex.Message}. The database may need to be initialized.";
            _messageClass = "error";
            Files = new List<FileMetadata>();
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
        }
    }

    protected async Task HandleFileSelected(InputFileChangeEventArgs e)
    {
        _selectedBrowserFile = e.GetMultipleFiles(1).FirstOrDefault();
        _uploadMessage = string.Empty;
        _messageClass = "";
        _uploadErrors.Clear();
        _isDuplicate = false;
        _duplicateFileInfo = null;
        _showDuplicateToast = false;
        StateHasChanged();
        await Task.CompletedTask;
    }

    protected async Task UploadFiles()
    {
        if (_isUploading || _selectedBrowserFile == null) return;

        try
        {
            _isUploading = true;
            _uploadProgress = 0;
            _uploadMessage = "Starting upload...";
            _messageClass = "info";
            _uploadErrors.Clear();
            
            // Clear previous duplicate detection state
            _isDuplicate = false;
            _duplicateFileInfo = null;
            _showDuplicateToast = false;
            
            StateHasChanged();

            // Simulate progress for user feedback
            for (int i = 10; i <= 30; i += 10)
            {
                _uploadProgress = i;
                StateHasChanged();
                await Task.Delay(100);
            }

            using var content = new MultipartFormDataContent();
            var fileContent = new StreamContent(_selectedBrowserFile.OpenReadStream(MaxFileSize));
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(_selectedBrowserFile.ContentType);
            content.Add(content: fileContent, name: "\"file\"", fileName: _selectedBrowserFile.Name);

            var client = ClientFactory.CreateClient();
            var response = await client.PostAsync("/api/FileUpload", content);

            _uploadProgress = 90;
            StateHasChanged();

            var result = await response.Content.ReadFromJsonAsync<FileUploadResult>();

            Logger.LogInformation("Upload result received: Success={Success}, IsDuplicate={IsDuplicate}, FileName={FileName}", 
                result.Success, result.IsDuplicate, result.FileName);

            if (result.Success)
            {
                _uploadProgress = 100;
                
                // Handle duplicate detection
                if (result.IsDuplicate)
                {
                    Logger.LogInformation("Duplicate detected - showing toast notification");
                    _isDuplicate = true;
                    _showDuplicateToast = true;
                    _duplicateFileInfo = new DuplicateFileInfo
                    {
                        FileName = result.ExistingFileName ?? "Unknown",
                        Size = result.Size,
                        UploadedAt = result.ExistingUploadedAt ?? DateTime.Now,
                        FileHash = result.FileHash ?? "Unknown"
                    };
                    
                    _uploadMessage = $"This file is identical to '{result.ExistingFileName}' and was not uploaded to prevent duplicates.";
                    _messageClass = "warning";
                    
                    Logger.LogInformation("Duplicate file detected: {FileName}, Existing: {ExistingFile}, Hash: {Hash}", 
                        result.FileName, result.ExistingFileName, result.FileHash);
                        
                    Logger.LogInformation("Toast state: _showDuplicateToast={ShowToast}, _isDuplicateDetected={IsDuplicate}", 
                        _showDuplicateToast, _isDuplicate);
                        
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
                    _uploadMessage = result.Message ?? $"File '{result.FileName}' uploaded successfully.";
                    _messageClass = "success";
                    Logger.LogInformation("File uploaded successfully: {FileName}, Size: {Size}, Hash: {Hash}", 
                        result.FileName, result.Size, result.FileHash);
                }

                _selectedBrowserFile = null;

                // Reload the file list only if it wasn't a duplicate
                if (!result.IsDuplicate)
                {
                    await LoadUploadedFiles();
                }
            }
            else
            {
                _uploadProgress = 0;
                _uploadMessage = "Upload Failed.";
                _messageClass = "error";
                _uploadErrors.Clear();
                Logger.LogError("Upload failed: {Error}", result.Error);

                if (!string.IsNullOrWhiteSpace(result.Error))
                {
                    try
                    {
                        // Try to parse error as JSON first
                        using var doc = JsonDocument.Parse(result.Error);
                        if (doc.RootElement.TryGetProperty("error", out var errorProp))
                        {
                            _uploadErrors.Add(errorProp.GetString() ?? "Unknown error");
                        }
                        else if (doc.RootElement.TryGetProperty("errors", out var errorsProp))
                        {
                            foreach (var error in errorsProp.EnumerateObject())
                            {
                                foreach (var msg in error.Value.EnumerateArray())
                                {
                                    _uploadErrors.Add(msg.GetString() ?? "Unknown error");
                                }
                            }
                        }
                        else
                        {
                            _uploadErrors.Add(result.Error);
                        }
                    }
                    catch (JsonException)
                    {
                        // Not JSON, just show the error string
                        _uploadErrors.Add(result.Error);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in UploadFile");
            _uploadMessage = $"Error: {ex.Message}";
            _messageClass = "error";
            _uploadErrors.Clear();
            _uploadErrors.Add(ex.Message);
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

    protected async Task DeleteFile(int id)
    {
        try
        {
            var success = await FileStorageService.DeleteFileAsync(id);
            if (success)
            {
                _uploadMessage = "File deleted successfully.";
                _messageClass = "success";
                
                // Clear duplicate detection state when showing other messages
                _isDuplicate = false;
                _duplicateFileInfo = null;
                _showDuplicateToast = false;
                
                await LoadUploadedFiles();
            }
            else
            {
                _uploadMessage = "Failed to delete file. File may not exist.";
                _messageClass = "error";
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting file");
            _uploadMessage = $"Error deleting file: {ex.Message}";
            _messageClass = "error";
        }
    }

    protected async Task HandleUrlUpload()
    {
        if (_isUploading) return;

        try
        {
            _isUploading = true;
            _uploadProgress = 0;
            _uploadMessage = "Adding URL...";
            _messageClass = "info";
            _uploadErrors.Clear();
            
            // Clear previous duplicate detection state
            _isDuplicate = false;
            _duplicateFileInfo = null;
            _showDuplicateToast = false;
            
            StateHasChanged();

            // Validate URL
            if (string.IsNullOrWhiteSpace(_fileUrl))
            {
                _uploadMessage = "Please enter a URL.";
                _messageClass = "error";
                _isUploading = false;
                StateHasChanged();
                return;
            }

            if (!Uri.TryCreate(_fileUrl, UriKind.Absolute, out var uri) || 
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                _uploadMessage = "Invalid URL format. URL must start with http:// or https://.";
                _messageClass = "error";
                _isUploading = false;
                StateHasChanged();
                return;
            }

            _uploadProgress = 30;
            StateHasChanged();

            // Call API to add URL
            var client = ClientFactory.CreateClient();
            var response = await client.PostAsJsonAsync("/api/FileUpload/url", new { Url = _fileUrl });

            _uploadProgress = 90;
            StateHasChanged();

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<UrlUploadResult>();
                
                _uploadProgress = 100;

                if (result?.Success == true)
                {
                    // Handle duplicate detection
                    if (result.IsDuplicate)
                    {
                        Logger.LogInformation("Duplicate URL detected - showing toast notification");
                        _isDuplicate = true;
                        _showDuplicateToast = true;
                        _duplicateFileInfo = new DuplicateFileInfo
                        {
                            FileName = result.ExistingFileName ?? "Unknown",
                            Size = 0,
                            UploadedAt = result.ExistingUploadedAt ?? DateTime.Now,
                            FileHash = string.Empty
                        };
                        
                        _uploadMessage = $"This URL already exists and was not added to prevent duplicates.";
                        _messageClass = "warning";
                        
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
                        _uploadMessage = result.Message ?? $"URL added successfully.";
                        _messageClass = "success";
                        Logger.LogInformation("URL added successfully: {Url}", _fileUrl);
                    }

                    // Clear the URL input
                    _fileUrl = string.Empty;

                    // Reload the file list only if it wasn't a duplicate
                    if (!result.IsDuplicate)
                    {
                        await LoadUploadedFiles();
                    }
                }
                else
                {
                    _uploadMessage = "Failed to add URL.";
                    _messageClass = "error";
                    if (!string.IsNullOrWhiteSpace(result?.Error))
                    {
                        _uploadErrors.Add(result.Error);
                    }
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _uploadMessage = "Failed to add URL.";
                _messageClass = "error";
                _uploadErrors.Add(errorContent);
                Logger.LogError("URL upload failed with status {StatusCode}: {Error}", response.StatusCode, errorContent);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error uploading URL: {Url}", _fileUrl);
            _uploadMessage = $"Error: {ex.Message}";
            _messageClass = "error";
            _uploadErrors.Clear();
            _uploadErrors.Add(ex.Message);
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

    private void ToggleUploadMode()
    {
        _isUrlMode = !_isUrlMode;
        _uploadMessage = string.Empty;
        _messageClass = "";
        _uploadErrors.Clear();
        _isDuplicate = false;
        _duplicateFileInfo = null;
        _showDuplicateToast = false;
        _fileUrl = string.Empty;
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

    protected void CloseDuplicateToast()
    {
        _showDuplicateToast = false;
        StateHasChanged();
    }

    // Helper methods for UI rendering
    private static string GetFileIcon(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "bi-file-earmark-pdf",
            ".docx" => "bi-file-earmark-word",
            ".doc" => "bi-file-earmark-word",
            ".txt" => "bi-file-earmark-text",
            ".md" => "bi-file-earmark-code",
            ".json" => "bi-file-earmark-code",
            ".xml" => "bi-file-earmark-code",
            ".csv" => "bi-file-earmark-spreadsheet",
            ".xlsx" => "bi-file-earmark-spreadsheet",
            ".xls" => "bi-file-earmark-spreadsheet",
            ".jpg" or ".jpeg" => "bi-file-earmark-image",
            ".png" => "bi-file-earmark-image",
            ".gif" => "bi-file-earmark-image",
            ".zip" => "bi-file-earmark-zip",
            ".rar" => "bi-file-earmark-zip",
            _ => "bi-file-earmark"
        };
    }

    private static string GetStatusClass(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "uploaded" => "status-uploaded",
            "pending" => "status-pending",
            "processing" => "status-processing",
            "error" => "status-error",
            _ => "status-pending"
        };
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

    public class UrlUploadResult
    {
        public bool Success { get; set; }
        public string? Url { get; set; }
        public string? FileName { get; set; }
        public string? Error { get; set; }
        public bool IsDuplicate { get; set; }
        public string? Message { get; set; }
        public string? ExistingFileName { get; set; }
        public int? ExistingFileId { get; set; }
        public DateTime? ExistingUploadedAt { get; set; }
    }
}
