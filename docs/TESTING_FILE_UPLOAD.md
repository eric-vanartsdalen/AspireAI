# File Upload Testing Guide

## Overview
This guide helps you test the newly implemented file upload functionality in AspireAI.

## Prerequisites
1. Application successfully builds and runs
2. Database is automatically initialized on startup
3. Data directory is created (default: `../data`)

## Testing Steps

### 1. Start the Application
```bash
# From the solution root directory
dotnet run --project src/AspireApp.AppHost
```

### 2. Verify Startup Logs
Look for these messages in the console output:
```
Created database directory: [path]
Created data directory: [path]
Database created successfully at: [path]
Database connection test successful
Database initialized with 0 existing files
```

### 3. Navigate to Upload Page
- Open browser to the Blazor app URL
- Click "Upload Documents" in the navigation menu
- Verify the upload page loads without errors

### 4. Debug JavaScript Loading (If Issues Occur)
If you see "JavaScript file upload function not found" errors:

**Open Browser Developer Tools (F12) and check:**
1. **Console Tab**: Look for JavaScript loading messages:
   ```
   upload-file.js loading...
   DOM Content Loaded - upload-file.js functions available
   upload-file.js loaded successfully
   ```

2. **Network Tab**: Verify `upload-file.js` loads successfully (200 status)

3. **Console Commands**: Test JavaScript functions manually:
   ```javascript
   // Test if functions are available
   window.testUploadFunctions()
   
   // Check individual functions
   typeof window.uploadFile
   typeof window.initializeFileUpload
   
   // Test file input exists
   document.getElementById('fileInput')
   ```

### 5. Test File Upload
**Prepare test files:**
- Create or find test files: `.pdf`, `.docx`, `.txt`, `.md`
- Ensure files are under 10MB (default limit)

**Upload process:**
1. Click "Choose File" or use the file input
2. Select a supported file type
3. Click "Upload File" button
4. Observe progress indicator
5. Verify success message appears
6. Check that the file appears in the "Uploaded Files" table

### 6. Verify File Storage
**Database verification:**
- Files should appear in the uploaded files list
- Metadata should show correct filename, size, and upload time

**Physical file verification:**
- Navigate to the data directory (default: `../data`)
- Verify the uploaded file exists with a unique filename
- Format should be: `originalname_YYYYMMDD_HHMMSS_uniqueid.ext`

### 7. Test File Deletion
1. In the uploaded files table, click "Delete" for any file
2. Confirm the file disappears from the list
3. Verify the physical file is removed from the data directory

### 8. Test Error Conditions
**File size limit:**
1. Try uploading a file larger than 10MB
2. Verify error message appears

**Unsupported file type:**
1. Try uploading a `.exe`, `.zip`, or other unsupported file
2. Verify error message appears

**No file selected:**
1. Click "Upload File" without selecting a file
2. Verify appropriate error message

### 9. Test API Endpoints (Optional)
Use a tool like Postman or curl to test the API directly:

**Upload file:**
```bash
curl -X POST -F "file=@testfile.pdf" http://localhost:5000/api/FileUpload
```

**Get files:**
```bash
curl http://localhost:5000/api/FileUpload
```

**Delete file:**
```bash
curl -X DELETE http://localhost:5000/api/FileUpload/1
```

## Expected Results

### Successful Upload Response
```json
{
  "success": true,
  "fileName": "document_20240115_143027_a1b2c3d4.pdf",
  "originalFileName": "document.pdf",
  "length": 102400,
  "id": 1,
  "uploadedAt": "2024-01-15T14:30:27.123Z"
}
```

### File List Response
```json
{
  "success": true,
  "files": [
    {
      "id": 1,
      "fileName": "document_20240115_143027_a1b2c3d4.pdf",
      "size": 102400,
      "uploadedAt": "2024-01-15T14:30:27.123Z"
    }
  ]
}
```

## Troubleshooting

### JavaScript Functions Not Found
**Symptoms:**
- "JavaScript file upload function not found" error
- "The value 'uploadFile' is not a function" in browser console

**Solutions:**
1. **Refresh the page** - JavaScript may not have loaded yet
2. **Check browser console** for loading errors
3. **Verify script inclusion** in `App.razor`:
   ```html
   <script src="@Assets["js/upload-file.js"]"></script>
   ```
4. **Test manually** in browser console:
   ```javascript
   window.testUploadFunctions()
   ```

### Upload Button Not Responding
- Check browser console for JavaScript errors
- Verify `upload-file.js` is loaded correctly
- Ensure the file input has a file selected
- Try refreshing the page

### Files Not Appearing in List
- Check application logs for database errors
- Verify database initialization completed successfully
- Refresh the page to reload the file list

### Physical Files Not Found
- Check the configured data directory path
- Verify application has write permissions to the directory
- Check application logs for file system errors

### API Errors
- Verify the application is running and accessible
- Check that MVC services are properly configured in `Program.cs`
- Look for controller registration and routing issues
- Test API endpoints directly with curl or Postman

### Browser Compatibility Issues
**Supported Browsers:**
- Chrome 60+
- Firefox 60+
- Edge 79+
- Safari 12+

**Features Required:**
- Fetch API support
- FormData support
- Modern JavaScript (ES6+)

### JavaScript Loading Race Conditions
If functions load inconsistently:
1. The component now retries JavaScript initialization up to 10 times
2. Check browser console for retry attempt messages
3. Slow networks may need more time - refresh if needed

## Console Debugging Commands

### Check JavaScript Status
```javascript
// Test all upload functions
window.testUploadFunctions()

// Check function availability
console.log({
    uploadFile: typeof window.uploadFile,
    initializeFileUpload: typeof window.initializeFileUpload,
    fileInput: !!document.getElementById('fileInput')
})
```

### Manual File Upload Test
```javascript
// Simulate file selection (after selecting file in UI)
const input = document.getElementById('fileInput');
if (input && input.files.length > 0) {
    console.log('Selected file:', input.files[0].name);
    window.uploadFile('fileInput', '/api/FileUpload').then(console.log);
}
```

## Next Steps
Once file upload is working correctly, you can proceed to:
1. Phase 4: Document processing pipeline
2. Phase 5: Vector storage and RAG implementation
3. Phase 6: Chat integration with uploaded documents