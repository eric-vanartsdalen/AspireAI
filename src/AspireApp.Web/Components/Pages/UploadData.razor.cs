using AspireApp.Web.Data;
using AspireApp.Web.Services;
using AspireApp.Web.Shared;
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
    protected bool _isUploading = false;
    private int _uploadProgress = 0;
    private readonly List<string> _uploadErrors = [];
    private readonly HashSet<int> _refreshingSourceIds = [];
    private bool _uploadControlsReady;
    
    // Duplicate detection tracking
    protected bool _isDuplicate;
    protected DuplicateFileInfo? _duplicateFileInfo;
    protected bool _showDuplicateToast;

    // Website URL upload property
    private string _websiteUrl = string.Empty;
    private static readonly string[] AllowedExtensions = [".pdf", ".docx", ".txt", ".md", ".json"];

    [Inject]
    public IConfiguration Configuration { get; set; } = default!;

    [Inject]
    public ILogger<UploadData> Logger { get; set; } = default!;

    [Inject]
    public IJSRuntime JSRuntime { get; set; } = default!;

    [Inject]
    public FileStorageService FileStorageService { get; set; } = default!;

    [Inject]
    public AspireApp.Web.Services.TenantContextService TenantContext { get; set; } = default!;

    private long MaxFileSize => Configuration.GetValue<long?>("FileUpload:MaxFileSize") ?? 10485760; // 10MB default

    protected bool IsFileSelected => _selectedBrowserFile != null;

    // Attribute dictionaries for conditional disabled attributes (used for attribute splatting)
    protected IReadOnlyDictionary<string, object> UploadButtonAttributes =>
        (!IsFileSelected || _isUploading)
            ? new Dictionary<string, object> { { "disabled", true } }
            : [];

    protected IReadOnlyDictionary<string, object> UrlInputAttributes =>
        (_isUploading)
            ? new Dictionary<string, object> { { "disabled", true } }
            : [];

    protected IReadOnlyDictionary<string, object> AddWebsiteButtonAttributes =>
        (string.IsNullOrWhiteSpace(_websiteUrl) || _isUploading)
            ? new Dictionary<string, object> { { "disabled", true } }
            : [];

    protected override async Task OnInitializedAsync()
    {
        TenantContext.OnTenantChanged += HandleTenantChanged;
        await TenantContext.EnsureInitializedAsync();

        if (Logger.IsEnabled(LogLevel.Information))
        {
            Logger.LogInformation("UploadData component initialized");
        }
        try
        {
            _objectReference = DotNetObjectReference.Create(this);
        }
        catch (Exception ex)
        {
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(ex, "Unable to create DotNetObjectReference");
            }
        }

        await LoadUploadedFiles();
    }

    private Task LoadUploadedFiles()
    {
        return LoadUploadedFiles(Logger);
    }

    private async Task LoadUploadedFiles(ILogger logger)
    {
        _isLoading = true;
        try
        {
            var initialized = await FileStorageService.EnsureInitializedAsync();
            if (!initialized)
            {
                _uploadMessage = "Database initialization failed. Please check the application logs.";
                _messageClass = "error";
                Files = [];
                return;
            }

            Files = await FileStorageService.GetAllFilesAsync(TenantContext.CurrentTenantId);
            if (logger.IsEnabled(LogLevel.Information))
            {
                var count = Files?.Count ?? 0;
                logger.LogInformation("Loaded {Count} uploaded files for tenant {TenantId}", count, TenantContext.CurrentTenantId);
            }

            if (Files != null)
            {
                _uploadMessage = string.Empty;
                _messageClass = "";
            }
        }
        catch (Exception ex)
        {
            if (Logger.IsEnabled(LogLevel.Error))
            {
                Logger.LogError(ex, "Error loading uploaded files");
            }
            _uploadMessage = $"Error loading files: {ex.Message}. The database may need to be initialized.";
            _messageClass = "error";
            Files = [];
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void HandleTenantChanged()
    {
        _ = InvokeAsync(async () =>
        {
            await LoadUploadedFiles();
            StateHasChanged();
        });
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && Logger.IsEnabled(LogLevel.Information))
        {
            Logger.LogInformation("UploadData component rendered for the first time");
        }

        if (firstRender)
        {
            _uploadControlsReady = true;
            await InvokeAsync(StateHasChanged);
        }
    }

    protected async Task HandleFileSelected(InputFileChangeEventArgs e)
    {
        var files = e.GetMultipleFiles(1);
        _selectedBrowserFile = files.Count > 0 ? files[0] : null;
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

            _isDuplicate = false;
            _duplicateFileInfo = null;
            _showDuplicateToast = false;

            StateHasChanged();

            for (int i = 10; i <= 30; i += 10)
            {
                _uploadProgress = i;
                StateHasChanged();
                await Task.Delay(100);
            }

            var result = await UploadFileAsync(_selectedBrowserFile);

            _uploadProgress = 90;
            StateHasChanged();

            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInformation("Upload result received: Success={Success}, IsDuplicate={IsDuplicate}, FileName={FileName}",
                    result.Success, result.IsDuplicate, result.FileName);
            }

            if (result.Success)
            {
                _uploadProgress = 100;

                if (result.IsDuplicate)
                {
                    if (Logger.IsEnabled(LogLevel.Information))
                    {
                        Logger.LogInformation("Duplicate detected - showing toast notification");
                    }
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

                    if (Logger.IsEnabled(LogLevel.Information))
                    {
                        Logger.LogInformation("Duplicate file detected: {FileName}, Existing: {ExistingFile}, Hash: {Hash}",
                            result.FileName, result.ExistingFileName, result.FileHash);

                        Logger.LogInformation("Toast state: _showDuplicateToast={ShowToast}, _isDuplicateDetected={IsDuplicate}",
                            _showDuplicateToast, _isDuplicate);
                    }

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
                    if (Logger.IsEnabled(LogLevel.Information))
                    {
                        Logger.LogInformation("File uploaded successfully - not a duplicate");
                        Logger.LogInformation("File uploaded successfully: {FileName}, Size: {Size}, Hash: {Hash}",
                            result.FileName, result.Size, result.FileHash);
                    }
                    _uploadMessage = result.Message ?? $"File '{result.FileName}' uploaded successfully.";
                    _messageClass = "success";
                }

                _selectedBrowserFile = null;

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
                if (Logger.IsEnabled(LogLevel.Error))
                {
                    Logger.LogError("Upload failed: {Error}", result.Error);
                }

                if (!string.IsNullOrWhiteSpace(result.Error))
                {
                    try
                    {
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
                        _uploadErrors.Add(result.Error);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (Logger.IsEnabled(LogLevel.Error))
            {
                Logger.LogError(ex, "Error in UploadFile");
            }
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
            await InvokeAsync(StateHasChanged);

            if (_uploadProgress == 100)
            {
                await Task.Delay(2000);
                _uploadProgress = 0;
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    protected async Task DeleteFile(int id)
    {
        try
        {
            var success = await FileStorageService.DeleteFileAsync(id, TenantContext.CurrentTenantId);
            if (success)
            {
                _uploadMessage = "File deleted successfully.";
                _messageClass = "success";

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
            if (Logger.IsEnabled(LogLevel.Error))
            {
                Logger.LogError(ex, "Error deleting file");
            }
            _uploadMessage = $"Error deleting file: {ex.Message}";
            _messageClass = "error";
        }
    }

    protected async Task RefreshWebSource(FileMetadata file)
    {
        if (!IsWebSource(file.SourceType) || !_refreshingSourceIds.Add(file.Id))
        {
            return;
        }

        try
        {
            _uploadErrors.Clear();
            _isDuplicate = false;
            _duplicateFileInfo = null;
            _showDuplicateToast = false;
            _uploadMessage = $"Refreshing '{file.FileName}'...";
            _messageClass = "info";
            await InvokeAsync(StateHasChanged);

            var refreshResult = await FileStorageService.RefreshWebSourceAsync(file.Id, TenantContext.CurrentTenantId);

            if (!refreshResult.Attempted)
            {
                _uploadMessage = $"Refresh reset '{file.FileName}', but automatic processing is currently unavailable.";
                _messageClass = "warning";
            }
            else if (refreshResult.Started)
            {
                _uploadMessage = $"Refresh requested for '{file.FileName}'. URL processing started automatically.";
                _messageClass = "success";
            }
            else
            {
                _uploadMessage = $"Refresh prepared for '{file.FileName}', but automatic processing could not be started.";
                _messageClass = "warning";

                if (!string.IsNullOrWhiteSpace(refreshResult.Detail))
                {
                    _uploadErrors.Add(refreshResult.Detail);
                }
            }

            await LoadUploadedFiles();
        }
        catch (InvalidOperationException ex)
        {
            _uploadErrors.Clear();
            _uploadMessage = ex.Message;
            _messageClass = "warning";
        }
        catch (Exception ex)
        {
            if (Logger.IsEnabled(LogLevel.Error))
            {
                Logger.LogError(ex, "Error refreshing URL source {FileId}", file.Id);
            }

            _uploadErrors.Clear();
            _uploadErrors.Add(ex.Message);
            _uploadMessage = $"Error refreshing URL source: {ex.Message}";
            _messageClass = "error";
        }
        finally
        {
            _refreshingSourceIds.Remove(file.Id);
            await InvokeAsync(StateHasChanged);
        }
    }

    protected async Task HandleUrlUpload()
    {
        if (_isUploading) return;

        try
        {
            _isUploading = true;
            _uploadProgress = 0;
            _uploadMessage = "Adding website URL...";
            _messageClass = "info";
            _uploadErrors.Clear();
            
            _isDuplicate = false;
            _duplicateFileInfo = null;
            _showDuplicateToast = false;
            
            StateHasChanged();

            if (string.IsNullOrWhiteSpace(_websiteUrl))
            {
                _uploadMessage = "Please enter a website URL.";
                _messageClass = "error";
                _isUploading = false;
                StateHasChanged();
                return;
            }

            if (!Uri.TryCreate(_websiteUrl, UriKind.Absolute, out var uri) || 
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

            var result = await UploadUrlAsync(_websiteUrl);

            _uploadProgress = 90;
            StateHasChanged();

            if (result.Success)
            {
                _uploadProgress = 100;

                if (result.IsDuplicate)
                {
                    if (Logger.IsEnabled(LogLevel.Information))
                    {
                        Logger.LogInformation("Duplicate website URL detected - showing toast notification");
                    }
                    _isDuplicate = true;
                    _showDuplicateToast = true;
                    _duplicateFileInfo = new DuplicateFileInfo
                    {
                        FileName = result.ExistingFileName ?? "Unknown",
                        Size = 0,
                        UploadedAt = result.ExistingUploadedAt ?? DateTime.Now,
                        FileHash = string.Empty
                    };

                    _uploadMessage = $"This website URL already exists and was not added to prevent duplicates.";
                    _messageClass = "warning";

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
                    _uploadMessage = result.Message ?? $"Website URL added successfully.";
                    _messageClass = "success";
                    if (Logger.IsEnabled(LogLevel.Information))
                    {
                        Logger.LogInformation("Website URL added successfully: {Url}", _websiteUrl);
                    }
                }

                _websiteUrl = string.Empty;

                if (!result.IsDuplicate)
                {
                    await LoadUploadedFiles();
                }
            }
            else
            {
                _uploadMessage = "Failed to add website URL.";
                _messageClass = "error";
                if (!string.IsNullOrWhiteSpace(result.Error))
                {
                    _uploadErrors.Add(result.Error);
                }
            }
        }
        catch (Exception ex)
        {
            if (Logger.IsEnabled(LogLevel.Error))
            {
                Logger.LogError(ex, "Error uploading website URL: {Url}", _websiteUrl);
            }
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
            await InvokeAsync(StateHasChanged);

            if (_uploadProgress == 100)
            {
                await Task.Delay(2000);
                _uploadProgress = 0;
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            TenantContext.OnTenantChanged -= HandleTenantChanged;
            _objectReference?.Dispose();
            Logger.LogInformation("UploadData component disposing");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error during component disposal");
        }
        GC.SuppressFinalize(this);
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        try
        {
            TenantContext.OnTenantChanged -= HandleTenantChanged;
            _objectReference?.Dispose();
            Logger.LogInformation("UploadData component disposing");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error during component disposal");
        }
        GC.SuppressFinalize(this);
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }

    protected void CloseDuplicateToast()
    {
        _showDuplicateToast = false;
        StateHasChanged();
    }

    // Blazor Server already runs this page on the server for the authenticated circuit.
    // Going back through our own controller with a fresh HttpClient loses the user's session,
    // so the interactive upload path writes through the scoped services directly.
    private async Task<FileUploadResult> UploadFileAsync(IBrowserFile browserFile)
    {
        if (browserFile.Size == 0)
        {
            return new FileUploadResult
            {
                Success = false,
                Error = "No file uploaded."
            };
        }

        if (browserFile.Size > MaxFileSize)
        {
            return new FileUploadResult
            {
                Success = false,
                Error = $"File size ({browserFile.Size:N0} bytes) exceeds maximum allowed size ({MaxFileSize:N0} bytes)."
            };
        }

        var fileExtension = Path.GetExtension(browserFile.Name).ToLowerInvariant();
        if (!AllowedExtensions.Contains(fileExtension, StringComparer.OrdinalIgnoreCase))
        {
            return new FileUploadResult
            {
                Success = false,
                Error = $"File type '{fileExtension}' is not allowed. Allowed types: {string.Join(", ", AllowedExtensions)}"
            };
        }
        
        var initialized = await FileStorageService.EnsureInitializedAsync();
        if (!initialized)
        {
            return new FileUploadResult
            {
                Success = false,
                Error = "File storage service initialization failed."
            };
        }

        await using var sourceStream = browserFile.OpenReadStream(MaxFileSize);
        await using var buffer = new MemoryStream();
        await sourceStream.CopyToAsync(buffer);

        buffer.Position = 0;
        var fileHash = FileStorageService.CalculateFileHash(buffer);
        if (Logger.IsEnabled(LogLevel.Information))
        {
            Logger.LogInformation("Calculated file hash: {Hash} for file: {FileName}", fileHash, browserFile.Name);
        }

        var existingFile = await FileStorageService.FindDuplicateByHashAsync(fileHash, TenantContext.CurrentTenantId);
        if (existingFile is not null)
        {
            return new FileUploadResult
            {
                Success = true,
                IsDuplicate = true,
                FileName = browserFile.Name,
                Size = browserFile.Size,
                ExistingFileId = existingFile.Id,
                ExistingFileName = existingFile.FileName,
                ExistingUploadedAt = existingFile.UploadedAt,
                FileHash = fileHash,
                Message = $"File already exists as '{existingFile.FileName}' (uploaded on {existingFile.UploadedAt:yyyy-MM-dd HH:mm:ss}). Duplicate not saved."
            };
        }

        var uniqueFileName = GenerateUniqueFileName(browserFile.Name);
        var filePath = Path.Combine(FileStorageService.DataDirectory, uniqueFileName);

        buffer.Position = 0;
        await using (var destinationStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await buffer.CopyToAsync(destinationStream);
        }

        var fileMetadata = await FileStorageService.AddFileAsync(
            uniqueFileName,
            browserFile.Name,
            FileStorageService.DataDirectory,
            browserFile.Size,
            fileHash,
            "uploaded",
            TenantContext.CurrentTenantId);

        QueueAutomaticProcessing(fileMetadata.Id, $"'{browserFile.Name}'");

        return new FileUploadResult
        {
            Success = true,
            IsDuplicate = false,
            FileName = uniqueFileName,
            Size = browserFile.Size,
            FileHash = fileHash,
            Message = "File uploaded successfully. Automatic processing is being queued.",
            ExistingFileId = fileMetadata.Id
        };
    }

    private async Task<UrlUploadResult> UploadUrlAsync(string websiteUrl)
    {
        if (string.IsNullOrWhiteSpace(websiteUrl))
        {
            return new UrlUploadResult
            {
                Success = false,
                Error = "No URL provided."
            };
        }

        if (!Uri.TryCreate(websiteUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return new UrlUploadResult
            {
                Success = false,
                Error = "Invalid URL format. URL must start with http:// or https://."
            };
        }

        var initialized = await FileStorageService.EnsureInitializedAsync();
        if (!initialized)
        {
            return new UrlUploadResult
            {
                Success = false,
                Error = "File storage service initialization failed."
            };
        }

        var urlHash = FileStorageService.CalculateUrlHash(websiteUrl);
        if (Logger.IsEnabled(LogLevel.Information))
        {
            Logger.LogInformation("Calculated URL hash: {Hash} for URL: {Url}", urlHash, websiteUrl);
        }

        var existingUrl = await FileStorageService.FindDuplicateByHashAsync(urlHash, TenantContext.CurrentTenantId);
        if (existingUrl is not null)
        {
            return new UrlUploadResult
            {
                Success = true,
                IsDuplicate = true,
                Url = websiteUrl,
                ExistingFileId = existingUrl.Id,
                ExistingFileName = existingUrl.FileName,
                ExistingUploadedAt = existingUrl.UploadedAt,
                Message = $"URL already exists as '{existingUrl.FileName}' (added on {existingUrl.UploadedAt:yyyy-MM-dd HH:mm:ss}). Duplicate not saved."
            };
        }

        var fileName = GenerateFileNameFromUrl(uri);
        var sourceType = UrlSourceTypeClassifier.Classify(websiteUrl);
        var fileMetadata = await FileStorageService.AddUrlAsync(
            fileName,
            websiteUrl,
            sourceType,
            UrlSourceTypeClassifier.GetDefaultMimeType(sourceType),
            "uploaded",
            TenantContext.CurrentTenantId);

        QueueAutomaticProcessing(fileMetadata.Id, $"'{fileName}'");

        return new UrlUploadResult
        {
            Success = true,
            IsDuplicate = false,
            Url = websiteUrl,
            FileName = fileName,
            Message = "Website URL added successfully. Automatic processing is being queued.",
            ExistingFileId = fileMetadata.Id
        };
    }

    private void QueueAutomaticProcessing(int fileId, string displayName)
    {
        _ = Task.Run(async () =>
        {
            var automaticProcessing = await FileStorageService.TryStartAutomaticProcessingAsync(fileId);

            await SafeRefreshAfterAutomaticProcessingAsync(displayName, automaticProcessing);
        });
    }

    private async Task SafeRefreshAfterAutomaticProcessingAsync(
        string displayName,
        AutomaticProcessingDispatchResult automaticProcessing)
    {
        try
        {
            await InvokeAsync(async () =>
            {
                if (automaticProcessing.Attempted && automaticProcessing.Started)
                {
                    await LoadUploadedFiles();
                    StateHasChanged();
                    return;
                }

                _uploadErrors.Clear();
                if (!string.IsNullOrWhiteSpace(automaticProcessing.Detail))
                {
                    _uploadErrors.Add(automaticProcessing.Detail);
                }

                _uploadMessage = automaticProcessing.Attempted
                    ? $"Saved {displayName}, but automatic processing could not be started."
                    : $"Saved {displayName}. Automatic processing is currently unavailable.";
                _messageClass = "warning";

                await LoadUploadedFiles();
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("renderer", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("disposed", StringComparison.OrdinalIgnoreCase))
        {
        }
    }

    private static string GenerateUniqueFileName(string originalFileName)
    {
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(originalFileName);
        var extension = Path.GetExtension(originalFileName);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var uniqueId = Guid.NewGuid().ToString("N")[..8];

        return $"{nameWithoutExtension}_{timestamp}_{uniqueId}{extension}";
    }

    private static string GenerateFileNameFromUrl(Uri uri)
    {
        var host = uri.Host.Replace("www.", "");
        var pathPart = uri.AbsolutePath.Trim('/').Replace("/", "_");

        if (string.IsNullOrEmpty(pathPart))
        {
            pathPart = "index";
        }
        else if (pathPart.Length > 50)
        {
            pathPart = pathPart[..50];
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        return $"{host}_{pathPart}_{timestamp}";
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

    private static string GetStatusBadgeClass(FileMetadata file)
    {
        return GetReadinessStatusOverride(file) switch
        {
            "queued" or "indexing" => "status-processing",
            "failed" or "timed_out" => "status-error",
            _ => GetStatusClass(file.Status)
        };
    }

    private static string GetStatusLabel(FileMetadata file)
    {
        return GetReadinessStatusOverride(file) switch
        {
            "queued" => "Index queued",
            "indexing" => "Indexing",
            "failed" => "Index failed",
            "timed_out" => "Index timed out",
            _ => GetStatusLabel(file.Status)
        };
    }

    private static string GetStatusClass(string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "uploaded" => "status-uploaded",
            "pending" => "status-pending",
            "processing" => "status-processing",
            "processed" => "status-processed",
            "error" => "status-error",
            _ => "status-pending"
        };
    }

    private static string GetStatusLabel(string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "uploaded" => "Uploaded",
            "pending" => "Pending",
            "processing" => "Processing",
            "processed" => "Processed",
            "error" => "Error",
            null or "" => "Unknown",
            _ => status
        };
    }

    private static string? GetReadinessStatusOverride(FileMetadata file)
    {
        if (!string.Equals(file.Status, "processed", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return file.IndexingStatus?.Trim().ToLowerInvariant() switch
        {
            "queued" => "queued",
            "indexing" => "indexing",
            "failed" => "failed",
            "timed_out" => "timed_out",
            _ => null
        };
    }

    private static bool IsWebSource(string? sourceType) =>
        UrlSourceTypeClassifier.IsWebSourceType(sourceType);

    private static string GetSourceBadgeClass(string? sourceType) =>
        IsWebSource(sourceType) ? "source-type-url" : "source-type-upload";

    private static string GetSourceBadgeLabel(string? sourceType) =>
        IsWebSource(sourceType) ? "WEB" : "FILE";

    private static string GetSourceValueClass(string? sourceType) =>
        IsWebSource(sourceType) ? "url-cell" : "file-size-cell";

    private bool IsRefreshingSource(int fileId) =>
        _refreshingSourceIds.Contains(fileId);

    private static string GetSourceIconTitle(string? sourceType) =>
        sourceType?.Trim().ToLowerInvariant() switch
        {
            UrlSourceTypeClassifier.YouTubeVideo => "YouTube Video",
            UrlSourceTypeClassifier.YouTubeChannel => "YouTube Channel",
            UrlSourceTypeClassifier.GenericUrl => "Website/Web Resource",
            _ => "Uploaded File"
        };

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
