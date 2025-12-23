using System.Security.Cryptography;
using Serilog;

namespace IrcClient.Core.Services;

/// <summary>
/// Provides secure file deletion by overwriting data before removal.
/// This makes data recovery significantly more difficult.
/// </summary>
/// <remarks>
/// <para>Features:</para>
/// <list type="bullet">
///   <item><description>Single-pass random overwrite (sufficient for modern storage)</description></item>
///   <item><description>Configurable enable/disable via settings</description></item>
///   <item><description>Falls back to normal deletion if overwrite fails</description></item>
///   <item><description>Works with both files and directories</description></item>
/// </list>
/// <para>Note: On SSDs, secure deletion is less effective due to wear leveling.
/// However, since all data is encrypted with AES-256-GCM, this provides defense-in-depth.</para>
/// </remarks>
public class SecureDeleteService
{
    private readonly ILogger _logger;
    
    /// <summary>
    /// Gets or sets whether secure deletion is enabled.
    /// When disabled, files are deleted normally.
    /// </summary>
    public bool IsEnabled { get; set; } = false;
    
    /// <summary>
    /// Number of overwrite passes (1 is sufficient for modern drives).
    /// </summary>
    public int OverwritePasses { get; set; } = 1;
    
    /// <summary>
    /// Buffer size for overwriting (4KB default).
    /// </summary>
    private const int BufferSize = 4096;
    
    /// <summary>
    /// Initializes a new instance of the SecureDeleteService.
    /// </summary>
    public SecureDeleteService()
    {
        _logger = SerilogConfig.ForContext<SecureDeleteService>();
    }
    
    /// <summary>
    /// Securely deletes a file by overwriting its contents before deletion.
    /// </summary>
    /// <param name="filePath">The path to the file to delete.</param>
    /// <returns>True if the file was deleted successfully.</returns>
    public bool DeleteFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return true; // File doesn't exist, consider it deleted
        }
        
        try
        {
            if (IsEnabled)
            {
                OverwriteFile(filePath);
            }
            
            File.Delete(filePath);
            _logger.Debug("Deleted file: {Path} (secure: {Secure})", filePath, IsEnabled);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete file: {Path}", filePath);
            
            // Try normal delete as fallback
            try
            {
                File.Delete(filePath);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
    
    /// <summary>
    /// Securely deletes a file asynchronously.
    /// </summary>
    /// <param name="filePath">The path to the file to delete.</param>
    /// <returns>True if the file was deleted successfully.</returns>
    public async Task<bool> DeleteFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return true;
        }
        
        try
        {
            if (IsEnabled)
            {
                await OverwriteFileAsync(filePath);
            }
            
            File.Delete(filePath);
            _logger.Debug("Deleted file: {Path} (secure: {Secure})", filePath, IsEnabled);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete file: {Path}", filePath);
            
            try
            {
                File.Delete(filePath);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
    
    /// <summary>
    /// Securely deletes a directory and all its contents.
    /// </summary>
    /// <param name="directoryPath">The path to the directory to delete.</param>
    /// <returns>True if the directory was deleted successfully.</returns>
    public async Task<bool> DeleteDirectoryAsync(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return true;
        }
        
        try
        {
            // Recursively delete all files
            foreach (var file in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
            {
                await DeleteFileAsync(file);
            }
            
            // Delete empty directories
            Directory.Delete(directoryPath, recursive: true);
            _logger.Debug("Deleted directory: {Path} (secure: {Secure})", directoryPath, IsEnabled);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete directory: {Path}", directoryPath);
            return false;
        }
    }
    
    /// <summary>
    /// Overwrites a file with random data for the specified number of passes.
    /// </summary>
    /// <param name="filePath">The path to the file to overwrite.</param>
    private void OverwriteFile(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        var length = fileInfo.Length;
        
        if (length == 0)
        {
            return; // Nothing to overwrite
        }
        
        // Remove read-only attribute if set
        if (fileInfo.IsReadOnly)
        {
            fileInfo.IsReadOnly = false;
        }
        
        var buffer = new byte[BufferSize];
        
        for (int pass = 0; pass < OverwritePasses; pass++)
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None);
            var remaining = length;
            
            while (remaining > 0)
            {
                var bytesToWrite = (int)Math.Min(BufferSize, remaining);
                RandomNumberGenerator.Fill(buffer.AsSpan(0, bytesToWrite));
                stream.Write(buffer, 0, bytesToWrite);
                remaining -= bytesToWrite;
            }
            
            stream.Flush();
        }
        
        // Clear the buffer
        CryptographicOperations.ZeroMemory(buffer);
        
        _logger.Debug("Overwrote file with {Passes} pass(es): {Path}", OverwritePasses, filePath);
    }
    
    /// <summary>
    /// Overwrites a file with random data asynchronously.
    /// </summary>
    /// <param name="filePath">The path to the file to overwrite.</param>
    private async Task OverwriteFileAsync(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        var length = fileInfo.Length;
        
        if (length == 0)
        {
            return;
        }
        
        if (fileInfo.IsReadOnly)
        {
            fileInfo.IsReadOnly = false;
        }
        
        var buffer = new byte[BufferSize];
        
        for (int pass = 0; pass < OverwritePasses; pass++)
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Write, 
                FileShare.None, BufferSize, useAsync: true);
            var remaining = length;
            
            while (remaining > 0)
            {
                var bytesToWrite = (int)Math.Min(BufferSize, remaining);
                RandomNumberGenerator.Fill(buffer.AsSpan(0, bytesToWrite));
                await stream.WriteAsync(buffer.AsMemory(0, bytesToWrite));
                remaining -= bytesToWrite;
            }
            
            await stream.FlushAsync();
        }
        
        CryptographicOperations.ZeroMemory(buffer);
        
        _logger.Debug("Overwrote file with {Passes} pass(es): {Path}", OverwritePasses, filePath);
    }
    
    /// <summary>
    /// Securely wipes a byte array in memory.
    /// </summary>
    /// <param name="data">The byte array to wipe.</param>
    public static void WipeMemory(byte[] data)
    {
        if (data != null)
        {
            CryptographicOperations.ZeroMemory(data);
        }
    }
}
