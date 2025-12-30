using System.Security.Cryptography;
using FluentAssertions;
using Munin.Core.Services;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for EncryptionService - AES-256-GCM encryption with PBKDF2 key derivation.
/// </summary>
public class EncryptionServiceTests
{
    [Fact]
    public void InitializeNew_GeneratesSaltAndUnlocksService()
    {
        var service = new EncryptionService();
        
        var salt = service.InitializeNew("TestPassword123");
        
        salt.Should().NotBeNull();
        salt.Should().HaveCount(32); // SaltSizeBytes
        service.IsUnlocked.Should().BeTrue();
        service.Salt.Should().NotBeNull();
    }

    [Fact]
    public void InitializeNew_ThrowsOnEmptyPassword()
    {
        var service = new EncryptionService();
        
        var act = () => service.InitializeNew("");
        
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Password cannot be empty*");
    }

    [Fact]
    public void Initialize_WithValidPasswordAndSalt_UnlocksService()
    {
        var service1 = new EncryptionService();
        var salt = service1.InitializeNew("TestPassword123");
        
        var service2 = new EncryptionService();
        service2.Initialize("TestPassword123", salt);
        
        service2.IsUnlocked.Should().BeTrue();
    }

    [Fact]
    public void Initialize_ThrowsOnInvalidSaltSize()
    {
        var service = new EncryptionService();
        var invalidSalt = new byte[16]; // Wrong size
        
        var act = () => service.Initialize("password", invalidSalt);
        
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Salt must be 32 bytes*");
    }

    [Fact]
    public void Encrypt_ReturnsValidEncryptedData()
    {
        var service = new EncryptionService();
        service.InitializeNew("TestPassword123");
        var plaintext = "Hello, World!"u8.ToArray();
        
        var encrypted = service.Encrypt(plaintext);
        
        encrypted.Should().NotBeNull();
        encrypted.Length.Should().BeGreaterThan(plaintext.Length);
        // Should contain magic bytes "IRCENC01" + nonce (12) + tag (16) + ciphertext
        encrypted.Length.Should().Be(8 + 12 + 16 + plaintext.Length);
    }

    [Fact]
    public void EncryptString_ReturnsValidEncryptedData()
    {
        var service = new EncryptionService();
        service.InitializeNew("TestPassword123");
        
        var encrypted = service.EncryptString("Hello, World!");
        
        encrypted.Should().NotBeNull();
        encrypted.Length.Should().BeGreaterThan(13); // UTF-8 string length
    }

    [Fact]
    public void Decrypt_ReturnsOriginalPlaintext()
    {
        var service = new EncryptionService();
        service.InitializeNew("TestPassword123");
        var plaintext = "Hello, World!"u8.ToArray();
        var encrypted = service.Encrypt(plaintext);
        
        var decrypted = service.Decrypt(encrypted);
        
        decrypted.Should().Equal(plaintext);
    }

    [Fact]
    public void DecryptString_ReturnsOriginalString()
    {
        var service = new EncryptionService();
        service.InitializeNew("TestPassword123");
        var plaintext = "Hello, World!";
        var encrypted = service.EncryptString(plaintext);
        
        var decrypted = service.DecryptString(encrypted);
        
        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void Decrypt_WithWrongPassword_ThrowsCryptographicException()
    {
        var service1 = new EncryptionService();
        var salt = service1.InitializeNew("CorrectPassword");
        var encrypted = service1.EncryptString("Secret Data");
        
        var service2 = new EncryptionService();
        service2.Initialize("WrongPassword", salt);
        
        var act = () => service2.Decrypt(encrypted);
        
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Decrypt_WithCorruptedData_ThrowsCryptographicException()
    {
        var service = new EncryptionService();
        service.InitializeNew("TestPassword123");
        var encrypted = service.EncryptString("Test Data");
        
        // Corrupt the ciphertext
        encrypted[encrypted.Length - 1] ^= 0xFF;
        
        var act = () => service.Decrypt(encrypted);
        
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Decrypt_WithInvalidMagicBytes_ThrowsCryptographicException()
    {
        var service = new EncryptionService();
        service.InitializeNew("TestPassword123");
        var encrypted = service.EncryptString("Test Data");
        
        // Corrupt magic bytes
        encrypted[0] = 0xFF;
        
        var act = () => service.Decrypt(encrypted);
        
        act.Should().Throw<CryptographicException>()
            .WithMessage("*Invalid encrypted data format*");
    }

    [Fact]
    public void Decrypt_WithTooShortData_ThrowsCryptographicException()
    {
        var service = new EncryptionService();
        service.InitializeNew("TestPassword123");
        var tooShort = new byte[10]; // Less than minimum
        
        var act = () => service.Decrypt(tooShort);
        
        act.Should().Throw<CryptographicException>()
            .WithMessage("*Invalid encrypted data format*");
    }

    [Fact]
    public void Lock_ClearsKeyAndLocksService()
    {
        var service = new EncryptionService();
        service.InitializeNew("TestPassword123");
        service.IsUnlocked.Should().BeTrue();
        
        service.Lock();
        
        service.IsUnlocked.Should().BeFalse();
    }

    [Fact]
    public void Encrypt_WhenLocked_ThrowsInvalidOperationException()
    {
        var service = new EncryptionService();
        
        var act = () => service.Encrypt("Test"u8.ToArray());
        
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Encryption service is locked*");
    }

    [Fact]
    public void Decrypt_WhenLocked_ThrowsInvalidOperationException()
    {
        var service = new EncryptionService();
        var encrypted = new byte[100];
        
        var act = () => service.Decrypt(encrypted);
        
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Encryption service is locked*");
    }

    [Fact]
    public void WipeMemory_ClearsSaltAndKey()
    {
        var service = new EncryptionService();
        service.InitializeNew("TestPassword123");
        service.Salt.Should().NotBeNull();
        
        service.WipeMemory();
        
        service.IsUnlocked.Should().BeFalse();
        service.Salt.Should().BeNull();
    }

    [Fact]
    public void VerifyPassword_WithCorrectPassword_ReturnsTrue()
    {
        var service = new EncryptionService();
        var salt = service.InitializeNew("TestPassword123");
        var verificationToken = service.CreateVerificationToken();
        
        var result = EncryptionService.VerifyPassword("TestPassword123", salt, verificationToken);
        
        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_WithWrongPassword_ReturnsFalse()
    {
        var service = new EncryptionService();
        var salt = service.InitializeNew("TestPassword123");
        var verificationToken = service.CreateVerificationToken();
        
        var result = EncryptionService.VerifyPassword("WrongPassword", salt, verificationToken);
        
        result.Should().BeFalse();
    }

    [Fact]
    public void IsEncrypted_WithEncryptedData_ReturnsTrue()
    {
        var service = new EncryptionService();
        service.InitializeNew("TestPassword123");
        var encrypted = service.EncryptString("Test");
        
        var result = EncryptionService.IsEncrypted(encrypted);
        
        result.Should().BeTrue();
    }

    [Fact]
    public void IsEncrypted_WithPlainData_ReturnsFalse()
    {
        var plainData = "Not encrypted"u8.ToArray();
        
        var result = EncryptionService.IsEncrypted(plainData);
        
        result.Should().BeFalse();
    }

    [Fact]
    public void IsEncrypted_WithTooShortData_ReturnsFalse()
    {
        var shortData = new byte[4];
        
        var result = EncryptionService.IsEncrypted(shortData);
        
        result.Should().BeFalse();
    }

    [Fact]
    public void ChangePassword_GeneratesNewSaltAndReKey()
    {
        var service = new EncryptionService();
        var oldSalt = service.InitializeNew("OldPassword");
        var testData = "Test Data";
        var encryptedWithOld = service.EncryptString(testData);
        
        var newSalt = service.ChangePassword("NewPassword");
        
        newSalt.Should().NotBeNull();
        newSalt.Should().NotEqual(oldSalt);
        service.IsUnlocked.Should().BeTrue();
        
        // Old encrypted data should not decrypt with new password
        var act = () => service.Decrypt(encryptedWithOld);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Encrypt_GeneratesUniqueNoncePerCall()
    {
        var service = new EncryptionService();
        service.InitializeNew("TestPassword123");
        var plaintext = "Same plaintext"u8.ToArray();
        
        var encrypted1 = service.Encrypt(plaintext);
        var encrypted2 = service.Encrypt(plaintext);
        
        // Should be different due to unique nonce
        encrypted1.Should().NotEqual(encrypted2);
    }

    [Fact]
    public void Encrypt_SupportsEmptyData()
    {
        var service = new EncryptionService();
        service.InitializeNew("TestPassword123");
        var empty = Array.Empty<byte>();
        
        var encrypted = service.Encrypt(empty);
        var decrypted = service.Decrypt(encrypted);
        
        decrypted.Should().BeEmpty();
    }

    [Fact]
    public void Encrypt_SupportsLargeData()
    {
        var service = new EncryptionService();
        service.InitializeNew("TestPassword123");
        var largeData = new byte[1024 * 1024]; // 1 MB
        RandomNumberGenerator.Fill(largeData);
        
        var encrypted = service.Encrypt(largeData);
        var decrypted = service.Decrypt(encrypted);
        
        decrypted.Should().Equal(largeData);
    }

    [Fact]
    public void Encrypt_SupportsUnicodeStrings()
    {
        var service = new EncryptionService();
        service.InitializeNew("TestPassword123");
        var unicode = "Hello 世界 🌍 Привет";
        
        var encrypted = service.EncryptString(unicode);
        var decrypted = service.DecryptString(encrypted);
        
        decrypted.Should().Be(unicode);
    }

    [Fact]
    public void DifferentPasswords_ProduceDifferentKeys()
    {
        var service1 = new EncryptionService();
        var salt = service1.InitializeNew("Password1");
        var encrypted1 = service1.EncryptString("Test");
        
        var service2 = new EncryptionService();
        service2.Initialize("Password2", salt);
        
        var act = () => service2.Decrypt(encrypted1);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void SameSaltDifferentPasswords_ProduceDifferentKeys()
    {
        var salt = RandomNumberGenerator.GetBytes(32);
        
        var service1 = new EncryptionService();
        service1.Initialize("Password1", salt);
        var encrypted = service1.EncryptString("Test");
        
        var service2 = new EncryptionService();
        service2.Initialize("Password2", salt);
        
        var act = () => service2.Decrypt(encrypted);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void IsFileEncrypted_WithNonExistentFile_ReturnsFalse()
    {
        var result = EncryptionService.IsFileEncrypted("nonexistent.dat");
        
        result.Should().BeFalse();
    }

    [Fact]
    public void CreateVerificationToken_CreatesValidToken()
    {
        var service = new EncryptionService();
        service.InitializeNew("TestPassword123");
        
        var token = service.CreateVerificationToken();
        
        token.Should().NotBeNull();
        token.Length.Should().BeGreaterThan(0);
        EncryptionService.IsEncrypted(token).Should().BeTrue();
    }

    [Fact]
    public void CreateVerificationToken_WhenLocked_Throws()
    {
        var service = new EncryptionService();
        
        var act = () => service.CreateVerificationToken();
        
        act.Should().Throw<InvalidOperationException>();
    }
}
