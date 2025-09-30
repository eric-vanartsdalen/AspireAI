using Microsoft.EntityFrameworkCore;

namespace AspireApp.Web.Data;

public class FileMetadata
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime UploadedAt { get; set; }
    public string Status { get; set; } = "Pending";
    public string FileHash { get; set; } = string.Empty;

    /// <summary>
    /// Gets a short representation of the file hash for display purposes
    /// </summary>
    public string ShortHash => string.IsNullOrEmpty(FileHash) ? "-" : FileHash.Substring(0, Math.Min(8, FileHash.Length));

    /// <summary>
    /// Checks if this file has a valid hash
    /// </summary>
    public bool HasHash => !string.IsNullOrEmpty(FileHash);

    /// <summary>
    /// Gets a formatted status display
    /// </summary>
    public string StatusDisplay => Status.ToUpperInvariant();
}
