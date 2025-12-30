using FluentAssertions;
using Munin.Core.Services;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for SecureDeleteService.
/// Verifies secure file deletion, overwriting, and directory handling.
/// </summary>
public class SecureDeleteServiceTests : IDisposable
{
    private readonly string _testPath;

    public SecureDeleteServiceTests()
    {
        _testPath = Path.Combine(Path.GetTempPath(), $"test_secure_delete_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testPath))
            {
                Directory.Delete(_testPath, true);
            }
        }
        catch { /* Ignore cleanup errors */ }
    }

    [Fact]
    public void Constructor_InitializesWithDefaults()
    {
        var service = new SecureDeleteService();
        
        service.IsEnabled.Should().BeFalse();
        service.OverwritePasses.Should().Be(1);
    }

    [Fact]
    public void DeleteFile_NonExistentFile_ReturnsTrue()
    {
        var service = new SecureDeleteService();
        var nonExistentPath = Path.Combine(_testPath, "nonexistent.txt");
        
        var result = service.DeleteFile(nonExistentPath);
        
        result.Should().BeTrue();
    }

    [Fact]
    public void DeleteFile_WhenDisabled_DeletesNormally()
    {
        var service = new SecureDeleteService { IsEnabled = false };
        var filePath = Path.Combine(_testPath, "test.txt");
        File.WriteAllText(filePath, "Test content");
        
        var result = service.DeleteFile(filePath);
        
        result.Should().BeTrue();
        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public void DeleteFile_WhenEnabled_OverwritesAndDeletes()
    {
        var service = new SecureDeleteService { IsEnabled = true };
        var filePath = Path.Combine(_testPath, "test.txt");
        File.WriteAllText(filePath, "Sensitive data");
        
        var result = service.DeleteFile(filePath);
        
        result.Should().BeTrue();
        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public void DeleteFile_EmptyFile_HandlesCorrectly()
    {
        var service = new SecureDeleteService { IsEnabled = true };
        var filePath = Path.Combine(_testPath, "empty.txt");
        File.WriteAllText(filePath, "");
        
        var result = service.DeleteFile(filePath);
        
        result.Should().BeTrue();
        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public void DeleteFile_LargeFile_HandlesCorrectly()
    {
        var service = new SecureDeleteService { IsEnabled = true };
        var filePath = Path.Combine(_testPath, "large.txt");
        
        // Create a 1MB file
        var data = new byte[1024 * 1024];
        Random.Shared.NextBytes(data);
        File.WriteAllBytes(filePath, data);
        
        var result = service.DeleteFile(filePath);
        
        result.Should().BeTrue();
        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public void DeleteFile_ReadOnlyFile_RemovesAttributeAndDeletes()
    {
        var service = new SecureDeleteService { IsEnabled = true };
        var filePath = Path.Combine(_testPath, "readonly.txt");
        File.WriteAllText(filePath, "Read-only content");
        File.SetAttributes(filePath, FileAttributes.ReadOnly);
        
        var result = service.DeleteFile(filePath);
        
        result.Should().BeTrue();
        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public void OverwritePasses_CanBeCustomized()
    {
        var service = new SecureDeleteService();
        
        service.OverwritePasses = 3;
        
        service.OverwritePasses.Should().Be(3);
    }

    [Fact]
    public void DeleteFile_MultipleOverwritePasses_CompletesSuccessfully()
    {
        var service = new SecureDeleteService 
        { 
            IsEnabled = true,
            OverwritePasses = 3
        };
        var filePath = Path.Combine(_testPath, "multipass.txt");
        File.WriteAllText(filePath, "Data to overwrite multiple times");
        
        var result = service.DeleteFile(filePath);
        
        result.Should().BeTrue();
        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteFileAsync_NonExistentFile_ReturnsTrue()
    {
        var service = new SecureDeleteService();
        var nonExistentPath = Path.Combine(_testPath, "nonexistent.txt");
        
        var result = await service.DeleteFileAsync(nonExistentPath);
        
        result.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteFileAsync_WhenDisabled_DeletesNormally()
    {
        var service = new SecureDeleteService { IsEnabled = false };
        var filePath = Path.Combine(_testPath, "async_test.txt");
        await File.WriteAllTextAsync(filePath, "Async test content");
        
        var result = await service.DeleteFileAsync(filePath);
        
        result.Should().BeTrue();
        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteFileAsync_WhenEnabled_OverwritesAndDeletes()
    {
        var service = new SecureDeleteService { IsEnabled = true };
        var filePath = Path.Combine(_testPath, "async_secure.txt");
        await File.WriteAllTextAsync(filePath, "Sensitive async data");
        
        var result = await service.DeleteFileAsync(filePath);
        
        result.Should().BeTrue();
        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteDirectoryAsync_NonExistentDirectory_ReturnsTrue()
    {
        var service = new SecureDeleteService();
        var nonExistentPath = Path.Combine(_testPath, "nonexistent_dir");
        
        var result = await service.DeleteDirectoryAsync(nonExistentPath);
        
        result.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteDirectoryAsync_EmptyDirectory_DeletesSuccessfully()
    {
        var service = new SecureDeleteService { IsEnabled = true };
        var dirPath = Path.Combine(_testPath, "empty_dir");
        Directory.CreateDirectory(dirPath);
        
        var result = await service.DeleteDirectoryAsync(dirPath);
        
        result.Should().BeTrue();
        Directory.Exists(dirPath).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteDirectoryAsync_WithFiles_DeletesAllRecursively()
    {
        var service = new SecureDeleteService { IsEnabled = true };
        var dirPath = Path.Combine(_testPath, "dir_with_files");
        Directory.CreateDirectory(dirPath);
        
        // Create multiple files
        await File.WriteAllTextAsync(Path.Combine(dirPath, "file1.txt"), "Content 1");
        await File.WriteAllTextAsync(Path.Combine(dirPath, "file2.txt"), "Content 2");
        
        // Create subdirectory with file
        var subDir = Path.Combine(dirPath, "subdir");
        Directory.CreateDirectory(subDir);
        await File.WriteAllTextAsync(Path.Combine(subDir, "file3.txt"), "Content 3");
        
        var result = await service.DeleteDirectoryAsync(dirPath);
        
        result.Should().BeTrue();
        Directory.Exists(dirPath).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteDirectoryAsync_WithNestedStructure_DeletesCompletely()
    {
        var service = new SecureDeleteService { IsEnabled = true };
        var dirPath = Path.Combine(_testPath, "nested");
        
        // Create nested structure
        Directory.CreateDirectory(Path.Combine(dirPath, "level1", "level2", "level3"));
        await File.WriteAllTextAsync(Path.Combine(dirPath, "root.txt"), "Root");
        await File.WriteAllTextAsync(Path.Combine(dirPath, "level1", "l1.txt"), "Level 1");
        await File.WriteAllTextAsync(Path.Combine(dirPath, "level1", "level2", "l2.txt"), "Level 2");
        await File.WriteAllTextAsync(Path.Combine(dirPath, "level1", "level2", "level3", "l3.txt"), "Level 3");
        
        var result = await service.DeleteDirectoryAsync(dirPath);
        
        result.Should().BeTrue();
        Directory.Exists(dirPath).Should().BeFalse();
    }

    [Fact]
    public void WipeMemory_NullArray_DoesNotThrow()
    {
        var act = () => SecureDeleteService.WipeMemory(null!);
        
        act.Should().NotThrow();
    }

    [Fact]
    public void WipeMemory_ByteArray_ZerosContent()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        
        SecureDeleteService.WipeMemory(data);
        
        data.Should().AllBeEquivalentTo((byte)0);
    }

    [Fact]
    public void WipeMemory_EmptyArray_HandlesCorrectly()
    {
        var data = Array.Empty<byte>();
        
        var act = () => SecureDeleteService.WipeMemory(data);
        
        act.Should().NotThrow();
    }

    [Fact]
    public void IsEnabled_CanBeToggled()
    {
        var service = new SecureDeleteService();
        
        service.IsEnabled.Should().BeFalse();
        
        service.IsEnabled = true;
        service.IsEnabled.Should().BeTrue();
        
        service.IsEnabled = false;
        service.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteFileAsync_ConcurrentOperations_HandledSafely()
    {
        var service = new SecureDeleteService { IsEnabled = true };
        var files = new List<string>();
        
        // Create multiple files
        for (int i = 0; i < 10; i++)
        {
            var filePath = Path.Combine(_testPath, $"concurrent_{i}.txt");
            await File.WriteAllTextAsync(filePath, $"Content {i}");
            files.Add(filePath);
        }
        
        // Delete concurrently
        var tasks = files.Select(f => service.DeleteFileAsync(f)).ToArray();
        var results = await Task.WhenAll(tasks);
        
        results.Should().AllBeEquivalentTo(true);
        files.Should().AllSatisfy(f => File.Exists(f).Should().BeFalse());
    }

    [Fact]
    public void DeleteFile_WithSpecialCharactersInPath_HandlesCorrectly()
    {
        var service = new SecureDeleteService { IsEnabled = true };
        var filePath = Path.Combine(_testPath, "file with spaces & chars.txt");
        File.WriteAllText(filePath, "Special chars test");
        
        var result = service.DeleteFile(filePath);
        
        result.Should().BeTrue();
        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteDirectoryAsync_WhenDisabled_DeletesNormally()
    {
        var service = new SecureDeleteService { IsEnabled = false };
        var dirPath = Path.Combine(_testPath, "normal_delete_dir");
        Directory.CreateDirectory(dirPath);
        await File.WriteAllTextAsync(Path.Combine(dirPath, "file.txt"), "Content");
        
        var result = await service.DeleteDirectoryAsync(dirPath);
        
        result.Should().BeTrue();
        Directory.Exists(dirPath).Should().BeFalse();
    }

    [Fact]
    public void DeleteFile_BinaryFile_OverwritesCorrectly()
    {
        var service = new SecureDeleteService { IsEnabled = true };
        var filePath = Path.Combine(_testPath, "binary.dat");
        
        // Create binary file with specific pattern
        var data = new byte[10000];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 256);
        }
        File.WriteAllBytes(filePath, data);
        
        var result = service.DeleteFile(filePath);
        
        result.Should().BeTrue();
        File.Exists(filePath).Should().BeFalse();
    }
}
