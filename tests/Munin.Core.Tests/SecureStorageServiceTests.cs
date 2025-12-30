using FluentAssertions;
using Munin.Core.Services;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for SecureStorageService - Secure file storage with encryption.
/// </summary>
public class SecureStorageServiceTests : IDisposable
{
    private readonly string _testBasePath;
    private const string TestPassword = "TestPassword123";

    public SecureStorageServiceTests()
    {
        _testBasePath = Path.Combine(Path.GetTempPath(), "MuninTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testBasePath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testBasePath))
            {
                Directory.Delete(_testBasePath, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public void Constructor_CreatesBasePath()
    {
        var service = new SecureStorageService(_testBasePath);
        
        Directory.Exists(_testBasePath).Should().BeTrue();
        service.BasePath.Should().Be(_testBasePath);
    }

    [Fact]
    public void IsEncryptionEnabled_DefaultIsFalse()
    {
        var service = new SecureStorageService(_testBasePath);
        
        service.IsEncryptionEnabled.Should().BeFalse();
    }

    [Fact]
    public void IsUnlocked_WhenNotEncrypted_ReturnsTrue()
    {
        var service = new SecureStorageService(_testBasePath);
        
        service.IsUnlocked.Should().BeTrue();
    }

    [Fact]
    public async Task WriteTextAsync_StoresPlaintextWhenNotEncrypted()
    {
        var service = new SecureStorageService(_testBasePath);
        var content = "Test content";
        
        await service.WriteTextAsync("test.txt", content);
        
        var read = await service.ReadTextAsync("test.txt");
        read.Should().Be(content);
    }

    [Fact]
    public async Task EnableEncryptionAsync_EnablesEncryption()
    {
        var service = new SecureStorageService(_testBasePath);
        
        var result = await service.EnableEncryptionAsync(TestPassword);
        
        result.Should().BeTrue();
        service.IsEncryptionEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task EnableEncryptionAsync_EncryptsExistingFiles()
    {
        var service = new SecureStorageService(_testBasePath);
        await service.WriteTextAsync("file1.txt", "Content 1");
        await service.WriteTextAsync("file2.txt", "Content 2");
        
        await service.EnableEncryptionAsync(TestPassword);
        
        // Files should now be encrypted on disk
        var rawBytes = await File.ReadAllBytesAsync(service.GetFullPath("file1.txt"));
        EncryptionService.IsEncrypted(rawBytes).Should().BeTrue();
    }

    [Fact]
    public async Task Unlock_WithCorrectPassword_Succeeds()
    {
        var service = new SecureStorageService(_testBasePath);
        await service.EnableEncryptionAsync(TestPassword);
        service.Lock();
        
        var result = service.Unlock(TestPassword);
        
        result.Should().BeTrue();
        service.IsUnlocked.Should().BeTrue();
    }

    [Fact]
    public async Task Unlock_WithWrongPassword_Fails()
    {
        var service = new SecureStorageService(_testBasePath);
        await service.EnableEncryptionAsync(TestPassword);
        service.Lock();
        
        var result = service.Unlock("WrongPassword");
        
        result.Should().BeFalse();
        service.IsUnlocked.Should().BeFalse();
    }

    [Fact]
    public async Task Lock_MakesServiceUnlocked()
    {
        var service = new SecureStorageService(_testBasePath);
        await service.EnableEncryptionAsync(TestPassword);
        service.IsUnlocked.Should().BeTrue();
        
        service.Lock();
        
        service.IsUnlocked.Should().BeFalse();
    }

    [Fact]
    public async Task WriteTextAsync_WhenEncrypted_EncryptsData()
    {
        var service = new SecureStorageService(_testBasePath);
        await service.EnableEncryptionAsync(TestPassword);
        
        await service.WriteTextAsync("encrypted.txt", "Secret content");
        
        // File should be encrypted on disk
        var rawBytes = await File.ReadAllBytesAsync(service.GetFullPath("encrypted.txt"));
        EncryptionService.IsEncrypted(rawBytes).Should().BeTrue();
        
        // But should decrypt transparently
        var content = await service.ReadTextAsync("encrypted.txt");
        content.Should().Be("Secret content");
    }

    [Fact]
    public async Task ReadTextAsync_WhenLocked_ThrowsException()
    {
        var service = new SecureStorageService(_testBasePath);
        await service.EnableEncryptionAsync(TestPassword);
        await service.WriteTextAsync("test.txt", "Content");
        service.Lock();
        
        var act = async () => await service.ReadTextAsync("test.txt");
        
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Storage is locked*");
    }

    [Fact]
    public async Task DisableEncryptionAsync_DecryptsAllFiles()
    {
        var service = new SecureStorageService(_testBasePath);
        await service.EnableEncryptionAsync(TestPassword);
        await service.WriteTextAsync("test.txt", "Content");
        
        var result = await service.DisableEncryptionAsync(TestPassword);
        
        result.Should().BeTrue();
        service.IsEncryptionEnabled.Should().BeFalse();
        
        // File should now be plaintext
        var rawBytes = await File.ReadAllBytesAsync(service.GetFullPath("test.txt"));
        EncryptionService.IsEncrypted(rawBytes).Should().BeFalse();
    }

    [Fact]
    public async Task ChangePasswordAsync_ReEncryptsAllFiles()
    {
        var service = new SecureStorageService(_testBasePath);
        await service.EnableEncryptionAsync(TestPassword);
        await service.WriteTextAsync("test.txt", "Secret");
        var oldContent = await service.ReadTextAsync("test.txt");
        
        var result = await service.ChangePasswordAsync(TestPassword, "NewPassword456");
        
        result.Should().BeTrue();
        
        // Should still be able to read with new password
        service.Lock();
        service.Unlock("NewPassword456").Should().BeTrue();
        var newContent = await service.ReadTextAsync("test.txt");
        newContent.Should().Be(oldContent);
        
        // Old password should not work
        service.Lock();
        service.Unlock(TestPassword).Should().BeFalse();
    }

    [Fact]
    public async Task FileExists_ReturnsTrueWhenFileExists()
    {
        var service = new SecureStorageService(_testBasePath);
        await service.WriteTextAsync("exists.txt", "Content");
        
        service.FileExists("exists.txt").Should().BeTrue();
        service.FileExists("notexists.txt").Should().BeFalse();
    }

    [Fact]
    public async Task DeleteFile_RemovesFile()
    {
        var service = new SecureStorageService(_testBasePath);
        await service.WriteTextAsync("todelete.txt", "Content");
        service.FileExists("todelete.txt").Should().BeTrue();
        
        service.DeleteFile("todelete.txt");
        
        service.FileExists("todelete.txt").Should().BeFalse();
    }

    [Fact]
    public async Task WriteBytesAsync_StoresBinaryData()
    {
        var service = new SecureStorageService(_testBasePath);
        var binaryData = new byte[] { 0x01, 0x02, 0x03, 0xFF };
        
        await service.WriteBytesAsync("binary.dat", binaryData);
        var read = await service.ReadBytesAsync("binary.dat");
        
        read.Should().Equal(binaryData);
    }

    [Fact]
    public async Task WriteBytesAsync_WhenEncrypted_EncryptsData()
    {
        var service = new SecureStorageService(_testBasePath);
        await service.EnableEncryptionAsync(TestPassword);
        var binaryData = new byte[] { 0x01, 0x02, 0x03 };
        
        await service.WriteBytesAsync("binary.dat", binaryData);
        
        var rawBytes = await File.ReadAllBytesAsync(service.GetFullPath("binary.dat"));
        EncryptionService.IsEncrypted(rawBytes).Should().BeTrue();
        
        var read = await service.ReadBytesAsync("binary.dat");
        read.Should().Equal(binaryData);
    }

    [Fact]
    public void WriteTextSync_StoresTextSynchronously()
    {
        var service = new SecureStorageService(_testBasePath);
        
        service.WriteTextSync("sync.txt", "Sync content");
        
        var read = service.ReadTextSync("sync.txt");
        read.Should().Be("Sync content");
    }

    [Fact]
    public async Task WriteTextSync_WhenEncrypted_EncryptsData()
    {
        var service = new SecureStorageService(_testBasePath);
        await service.EnableEncryptionAsync(TestPassword);
        
        service.WriteTextSync("sync.txt", "Encrypted sync");
        
        var rawBytes = await File.ReadAllBytesAsync(service.GetFullPath("sync.txt"));
        EncryptionService.IsEncrypted(rawBytes).Should().BeTrue();
    }

    [Fact]
    public void ReadBytesSync_ReadsDataSynchronously()
    {
        var service = new SecureStorageService(_testBasePath);
        var data = new byte[] { 1, 2, 3 };
        File.WriteAllBytes(service.GetFullPath("sync.dat"), data);
        
        var read = service.ReadBytesSync("sync.dat");
        
        read.Should().Equal(data);
    }

    [Fact]
    public async Task ReadTextAsync_NonExistentFile_ReturnsNull()
    {
        var service = new SecureStorageService(_testBasePath);
        
        var content = await service.ReadTextAsync("notexists.txt");
        
        content.Should().BeNull();
    }

    [Fact]
    public async Task ReadBytesAsync_NonExistentFile_ReturnsNull()
    {
        var service = new SecureStorageService(_testBasePath);
        
        var bytes = await service.ReadBytesAsync("notexists.dat");
        
        bytes.Should().BeNull();
    }

    [Fact]
    public void ResetAll_DeletesAllData()
    {
        var service = new SecureStorageService(_testBasePath);
        service.WriteTextSync("file1.txt", "Content 1");
        service.WriteTextSync("file2.txt", "Content 2");
        
        service.ResetAll();
        
        Directory.Exists(_testBasePath).Should().BeTrue();
        Directory.GetFiles(_testBasePath).Should().BeEmpty();
    }

    [Fact]
    public async Task GetFullPath_CreatesSubdirectories()
    {
        var service = new SecureStorageService(_testBasePath);
        
        await service.WriteTextAsync("sub/dir/file.txt", "Content");
        
        service.FileExists("sub/dir/file.txt").Should().BeTrue();
        var fullPath = service.GetFullPath("sub/dir/file.txt");
        File.Exists(fullPath).Should().BeTrue();
    }

    [Fact]
    public async Task EnableEncryptionAsync_WhenAlreadyEnabled_ReturnsFalse()
    {
        var service = new SecureStorageService(_testBasePath);
        await service.EnableEncryptionAsync(TestPassword);
        
        var result = await service.EnableEncryptionAsync(TestPassword);
        
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DisableEncryptionAsync_WithWrongPassword_ReturnsFalse()
    {
        var service = new SecureStorageService(_testBasePath);
        await service.EnableEncryptionAsync(TestPassword);
        
        var result = await service.DisableEncryptionAsync("WrongPassword");
        
        result.Should().BeFalse();
        service.IsEncryptionEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task ChangePasswordAsync_WithWrongCurrentPassword_ReturnsFalse()
    {
        var service = new SecureStorageService(_testBasePath);
        await service.EnableEncryptionAsync(TestPassword);
        
        var result = await service.ChangePasswordAsync("WrongPassword", "NewPassword");
        
        result.Should().BeFalse();
    }

    [Fact]
    public async Task EncryptionPersists_AcrossInstances()
    {
        // Enable encryption in first instance
        var service1 = new SecureStorageService(_testBasePath);
        await service1.EnableEncryptionAsync(TestPassword);
        await service1.WriteTextAsync("test.txt", "Secret data");
        
        // Create new instance pointing to same path
        var service2 = new SecureStorageService(_testBasePath);
        
        service2.IsEncryptionEnabled.Should().BeTrue();
        service2.Unlock(TestPassword).Should().BeTrue();
        var content = await service2.ReadTextAsync("test.txt");
        content.Should().Be("Secret data");
    }

    [Fact]
    public async Task MultipleFiles_AllEncryptedCorrectly()
    {
        var service = new SecureStorageService(_testBasePath);
        await service.EnableEncryptionAsync(TestPassword);
        
        await service.WriteTextAsync("file1.txt", "Content 1");
        await service.WriteTextAsync("file2.txt", "Content 2");
        await service.WriteTextAsync("sub/file3.txt", "Content 3");
        
        var content1 = await service.ReadTextAsync("file1.txt");
        var content2 = await service.ReadTextAsync("file2.txt");
        var content3 = await service.ReadTextAsync("sub/file3.txt");
        
        content1.Should().Be("Content 1");
        content2.Should().Be("Content 2");
        content3.Should().Be("Content 3");
    }

    [Fact]
    public async Task WriteThenLockThenUnlock_PreservesData()
    {
        var service = new SecureStorageService(_testBasePath);
        await service.EnableEncryptionAsync(TestPassword);
        await service.WriteTextAsync("test.txt", "Preserved");
        
        service.Lock();
        service.Unlock(TestPassword);
        
        var content = await service.ReadTextAsync("test.txt");
        content.Should().Be("Preserved");
    }
}
