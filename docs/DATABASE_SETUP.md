# Database Setup Guide

## Overview
AspireAI uses SQLite for storing file metadata and implements a complete file upload system with REST API endpoints. The application automatically creates and initializes the database on startup.

## Default Configuration
- **Database Path**: `../database/data-resources.db` (relative to the Web project)
- **Data Directory**: `../data` (for uploaded files)
- **Connection String**: Configured in `appsettings.json`
- **File Upload API**: `/api/FileUpload` endpoint

## Automatic Initialization
The application will automatically:
1. Create the database directory if it doesn't exist
2. Create the data directory for file storage
3. Initialize the SQLite database with required tables
4. Test the database connection on startup

## File Upload Features
- **Web UI**: User-friendly upload interface with drag-and-drop support
- **File Validation**: Size limits (10MB default) and extension restrictions
- **Progress Tracking**: Visual feedback during upload process
- **Database Integration**: Automatic metadata storage for uploaded files
- **File Management**: View and delete uploaded files through the UI

## API Endpoints

### Upload File
- **POST** `/api/FileUpload`
- **Content-Type**: `multipart/form-data`
- **Parameters**: `file` (form data)
- **Response**: JSON with upload result and file metadata

### Get Uploaded Files
- **GET** `/api/FileUpload`
- **Response**: JSON list of all uploaded file metadata

### Delete File
- **DELETE** `/api/FileUpload/{id}`
- **Parameters**: `id` (file ID)
- **Response**: JSON confirmation of deletion

## Configuration
You can customize the database and file storage locations in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=../database/data-resources.db"
  },
  "FileUpload": {
    "DataDirectory": "../data",
    "MaxFileSize": 10485760,
    "AllowedExtensions": [ "*.pdf", "*.docx", "*.txt", "*.md" ]
  }
}
```

## File Naming and Storage
- **Unique Filenames**: Files are automatically renamed to prevent conflicts
- **Format**: `originalname_YYYYMMDD_HHMMSS_uniqueid.ext`
- **Example**: `document_20240115_143027_a1b2c3d4.pdf`
- **Physical Storage**: Files stored in the configured data directory
- **Database Metadata**: Original filename, size, upload timestamp stored in SQLite

## Supported File Types
- **PDF Documents**: `.pdf`
- **Word Documents**: `.docx`
- **Text Files**: `.txt`
- **Markdown Files**: `.md`

Additional file types can be configured in `appsettings.json`.

## Troubleshooting

### SQLite Error 14: 'unable to open database file'
This error occurs when:
- The database directory doesn't exist
- The application doesn't have write permissions
- The database file path is invalid

**Solution**: The application now automatically creates directories and initializes the database. If you still see this error:
1. Check that the application has write permissions to the specified directory
2. Verify the connection string path in `appsettings.json`
3. Check application startup logs for initialization messages

### File Upload Issues
Common upload problems and solutions:

**File size too large**:
- Check the `MaxFileSize` setting in `appsettings.json`
- Default limit is 10MB (10485760 bytes)

**File type not allowed**:
- Verify the file extension is in the `AllowedExtensions` list
- Update configuration to allow additional file types if needed

**Upload fails with network error**:
- Check that the web server is running
- Verify the `/api/FileUpload` endpoint is accessible
- Check browser developer tools for specific error messages

### Manual Database Creation
If automatic initialization fails, you can manually create the database:
1. Create the directory: `mkdir database`
2. The application will create the database file and tables on next startup

## Database Schema
The application uses Entity Framework Core with the following entities:
- `FileMetadata`: Stores information about uploaded files
  - `Id` (int): Primary key
  - `FileName` (string): Stored filename (unique)
  - `Size` (long): File size in bytes
  - `UploadedAt` (DateTime): Upload timestamp

## Phase 3 Implementation Status
This database setup is part of Phase 3 of the AspireAI roadmap:
- ? File upload UI with progress tracking
- ? SQLite database for metadata storage
- ? Automatic database initialization
- ? REST API endpoints for file operations
- ? File validation and error handling
- ? Unique filename generation to prevent conflicts
- ? Document processing pipeline (future phase)
- ? RAG integration (future phase)

## Security Considerations
- File uploads are validated for type and size
- Unique filenames prevent path traversal attacks
- Files are stored outside the web root directory
- API endpoints include proper error handling and logging

## Future Enhancements
Planned improvements for subsequent phases:
- Document content extraction and indexing
- Vector embeddings for RAG functionality
- Advanced file search and filtering
- Batch upload capabilities
- File preview and thumbnail generation