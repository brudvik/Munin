using FluentAssertions;
using Munin.Core.Services;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for FishCryptService - FiSH encryption compatible with mIRC/HexChat.
/// </summary>
public class FishCryptServiceTests
{
    private const string TestKey = "SecretKey123";
    private const string ServerId = "irc.example.com";
    private const string Target = "#testchannel";

    [Fact]
    public void SetKey_StoresKeyForTarget()
    {
        var service = new FishCryptService();
        
        service.SetKey(ServerId, Target, TestKey);
        
        var retrievedKey = service.GetKey(ServerId, Target);
        retrievedKey.Should().Be(TestKey);
    }

    [Fact]
    public void SetKey_WithNull_RemovesKey()
    {
        var service = new FishCryptService();
        service.SetKey(ServerId, Target, TestKey);
        
        service.SetKey(ServerId, Target, null);
        
        service.HasKey(ServerId, Target).Should().BeFalse();
        service.GetKey(ServerId, Target).Should().BeNull();
    }

    [Fact]
    public void HasKey_ReturnsTrueWhenKeyExists()
    {
        var service = new FishCryptService();
        service.SetKey(ServerId, Target, TestKey);
        
        service.HasKey(ServerId, Target).Should().BeTrue();
    }

    [Fact]
    public void HasKey_ReturnsFalseWhenNoKey()
    {
        var service = new FishCryptService();
        
        service.HasKey(ServerId, Target).Should().BeFalse();
    }

    [Fact]
    public void SetKey_RaisesKeyChangedEvent()
    {
        var service = new FishCryptService();
        var eventRaised = false;
        service.KeyChanged += (s, e) =>
        {
            e.ServerId.Should().Be(ServerId);
            e.Target.Should().Be(Target);
            e.HasKey.Should().BeTrue();
            eventRaised = true;
        };
        
        service.SetKey(ServerId, Target, TestKey);
        
        eventRaised.Should().BeTrue();
    }

    [Fact]
    public void Encrypt_WithNoKey_ReturnsNull()
    {
        var service = new FishCryptService();
        
        var encrypted = service.Encrypt(ServerId, Target, "Test message");
        
        encrypted.Should().BeNull();
    }

    [Fact]
    public void Encrypt_Static_ReturnsCbcPrefixedMessage()
    {
        var encrypted = FishCryptService.Encrypt("Hello World", TestKey, useCbc: true, useMircryptionFormat: false);
        
        encrypted.Should().NotBeNull();
        encrypted.Should().StartWith(FishCryptService.CbcPrefix);
    }

    [Fact]
    public void Encrypt_Static_EcbMode_ReturnsEcbPrefixedMessage()
    {
        var encrypted = FishCryptService.Encrypt("Hello World", TestKey, useCbc: false);
        
        encrypted.Should().NotBeNull();
        encrypted.Should().StartWith(FishCryptService.EcbPrefix);
    }

    [Fact]
    public void Decrypt_Static_DecryptsEcbMessage()
    {
        var plaintext = "Hello World";
        var encrypted = FishCryptService.Encrypt(plaintext, TestKey, useCbc: false);
        
        var decrypted = FishCryptService.Decrypt(encrypted, TestKey);
        
        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void Decrypt_Static_DecryptsCbcMessage()
    {
        var plaintext = "Hello World";
        var encrypted = FishCryptService.Encrypt(plaintext, TestKey, useCbc: true, useMircryptionFormat: false);
        
        var decrypted = FishCryptService.Decrypt(encrypted, TestKey);
        
        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void Decrypt_Static_DecryptsMircryptionCbcMessage()
    {
        var plaintext = "Hello World";
        var encrypted = FishCryptService.Encrypt(plaintext, TestKey, useCbc: true, useMircryptionFormat: true);
        
        var decrypted = FishCryptService.Decrypt(encrypted, TestKey);
        
        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void Decrypt_WithNoKey_ReturnsNull()
    {
        var service = new FishCryptService();
        var encrypted = FishCryptService.Encrypt("Test", TestKey);
        
        var decrypted = service.Decrypt(ServerId, Target, encrypted);
        
        decrypted.Should().BeNull();
    }

    [Fact]
    public void Decrypt_WithKey_DecryptsMessage()
    {
        var service = new FishCryptService();
        service.SetKey(ServerId, Target, TestKey);
        var plaintext = "Test message";
        var encrypted = FishCryptService.Encrypt(plaintext, TestKey);
        
        var decrypted = service.Decrypt(ServerId, Target, encrypted);
        
        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void IsEncrypted_WithEcbPrefix_ReturnsTrue()
    {
        var encrypted = FishCryptService.Encrypt("Test", TestKey, useCbc: false);
        
        FishCryptService.IsEncrypted(encrypted).Should().BeTrue();
    }

    [Fact]
    public void IsEncrypted_WithCbcPrefix_ReturnsTrue()
    {
        var encrypted = FishCryptService.Encrypt("Test", TestKey, useCbc: true, useMircryptionFormat: false);
        
        FishCryptService.IsEncrypted(encrypted).Should().BeTrue();
    }

    [Fact]
    public void IsEncrypted_WithMcpsPrefix_ReturnsTrue()
    {
        var message = "mcps test";
        
        FishCryptService.IsEncrypted(message).Should().BeTrue();
    }

    [Fact]
    public void IsEncrypted_WithPlainText_ReturnsFalse()
    {
        FishCryptService.IsEncrypted("Plain text message").Should().BeFalse();
    }

    [Fact]
    public void FishBase64Encode_EncodesCorrectly()
    {
        var data = new byte[] { 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0 };
        
        var encoded = FishCryptService.FishBase64Encode(data);
        
        encoded.Should().NotBeNullOrEmpty();
        encoded.Length.Should().Be(12); // 8 bytes -> 12 characters
    }

    [Fact]
    public void FishBase64Decode_DecodesCorrectly()
    {
        var data = new byte[] { 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0 };
        var encoded = FishCryptService.FishBase64Encode(data);
        
        var decoded = FishCryptService.FishBase64Decode(encoded);
        
        decoded.Should().Equal(data);
    }

    [Fact]
    public void FishBase64RoundTrip_PreservesData()
    {
        var originalData = new byte[24];
        Random.Shared.NextBytes(originalData);
        
        var encoded = FishCryptService.FishBase64Encode(originalData);
        var decoded = FishCryptService.FishBase64Decode(encoded);
        
        decoded.Should().Equal(originalData);
    }

    [Fact]
    public void Encrypt_DifferentMessages_ProduceDifferentCiphertext()
    {
        var encrypted1 = FishCryptService.Encrypt("Message 1", TestKey);
        var encrypted2 = FishCryptService.Encrypt("Message 2", TestKey);
        
        encrypted1.Should().NotBe(encrypted2);
    }

    [Fact]
    public void Encrypt_SameMessageMultipleTimes_ProducesDifferentCiphertext()
    {
        var message = "Same message";
        var encrypted1 = FishCryptService.Encrypt(message, TestKey);
        var encrypted2 = FishCryptService.Encrypt(message, TestKey);
        
        // CBC mode with random IV should produce different ciphertext
        encrypted1.Should().NotBe(encrypted2);
    }

    [Fact]
    public void Decrypt_WithWrongKey_ReturnsNullOrGarbage()
    {
        var encrypted = FishCryptService.Encrypt("Test", "CorrectKey");
        
        var decrypted = FishCryptService.Decrypt(encrypted, "WrongKey");
        
        // With wrong key, may return null or garbage data
        if (decrypted != null)
        {
            decrypted.Should().NotBe("Test");
        }
    }

    [Fact]
    public void Decrypt_WithInvalidFormat_ReturnsNull()
    {
        var decrypted = FishCryptService.Decrypt("Invalid message", TestKey);
        
        decrypted.Should().BeNull();
    }

    [Fact]
    public void EncryptDecrypt_SupportsUnicodeCharacters()
    {
        var plaintext = "Hello 世界 🌍 Привет";
        var encrypted = FishCryptService.Encrypt(plaintext, TestKey);
        
        var decrypted = FishCryptService.Decrypt(encrypted, TestKey);
        
        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void EncryptDecrypt_SupportsLongMessages()
    {
        var plaintext = new string('A', 1000);
        var encrypted = FishCryptService.Encrypt(plaintext, TestKey);
        
        var decrypted = FishCryptService.Decrypt(encrypted, TestKey);
        
        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void GetAllKeys_ReturnsAllStoredKeys()
    {
        var service = new FishCryptService();
        service.SetKey("server1", "#channel1", "key1");
        service.SetKey("server2", "#channel2", "key2");
        
        var keys = service.GetAllKeys();
        
        keys.Should().HaveCount(2);
    }

    [Fact]
    public void LoadKeys_ImportsKeys()
    {
        var keysToLoad = new Dictionary<string, string>
        {
            ["server1:#channel1"] = "key1",
            ["server2:#channel2"] = "key2"
        };
        var service = new FishCryptService();
        
        service.LoadKeys(keysToLoad);
        
        service.HasKey("server1", "#channel1").Should().BeTrue();
        service.HasKey("server2", "#channel2").Should().BeTrue();
        service.GetKey("server1", "#channel1").Should().Be("key1");
    }

    [Fact]
    public void ExportKeys_ReturnsAllKeys()
    {
        var service = new FishCryptService();
        service.SetKey("server1", "#channel1", "key1");
        service.SetKey("server2", "#channel2", "key2");
        
        var exported = service.ExportKeys();
        
        exported.Should().HaveCount(2);
        exported.Should().ContainKey("server1:#channel1");
        exported.Should().ContainKey("server2:#channel2");
    }

    [Fact]
    public void ImportKeys_ReplacesExistingKeys()
    {
        var service = new FishCryptService();
        service.SetKey("server1", "#channel1", "oldkey");
        
        var newKeys = new Dictionary<string, string>
        {
            ["server2:#channel2"] = "newkey"
        };
        service.ImportKeys(newKeys);
        
        service.HasKey("server1", "#channel1").Should().BeFalse();
        service.HasKey("server2", "#channel2").Should().BeTrue();
    }

    [Fact]
    public void SetKey_WithCbcPrefix_StoresKeyWithPrefix()
    {
        var service = new FishCryptService();
        var keyWithPrefix = "cbc:SecretKey123";
        
        service.SetKey(ServerId, Target, keyWithPrefix);
        
        service.GetKey(ServerId, Target).Should().Be(keyWithPrefix);
    }

    [Fact]
    public void Encrypt_WithCbcPrefixedKey_UsesCbcMode()
    {
        var service = new FishCryptService();
        service.SetKey(ServerId, Target, "cbc:" + TestKey);
        
        var encrypted = service.Encrypt(ServerId, Target, "Test message");
        
        encrypted.Should().NotBeNull();
        // Should use CBC mode (either *OK or +OK *)
        (encrypted!.StartsWith(FishCryptService.CbcPrefix) || 
         encrypted.StartsWith(FishCryptService.EcbPrefix + "*")).Should().BeTrue();
    }

    [Fact]
    public void Decrypt_WithCbcPrefixedKey_DecodesCorrectly()
    {
        var service = new FishCryptService();
        service.SetKey(ServerId, Target, "cbc:" + TestKey);
        var plaintext = "Test message";
        
        // Encrypt with the actual key (without prefix)
        var encrypted = FishCryptService.Encrypt(plaintext, TestKey, useCbc: true);
        
        // Decrypt using service (should strip cbc: prefix from key)
        var decrypted = service.Decrypt(ServerId, Target, encrypted);
        
        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void KeyLookup_IsCaseInsensitive()
    {
        var service = new FishCryptService();
        service.SetKey(ServerId, "#TestChannel", TestKey);
        
        service.HasKey(ServerId, "#testchannel").Should().BeTrue();
        service.HasKey(ServerId, "#TESTCHANNEL").Should().BeTrue();
        service.GetKey(ServerId, "#TeStChAnNeL").Should().Be(TestKey);
    }

    [Fact]
    public void EncryptDecrypt_EcbMode_RoundTrip()
    {
        var plaintext = "ECB mode test message";
        var encrypted = FishCryptService.Encrypt(plaintext, TestKey, useCbc: false);
        var decrypted = FishCryptService.Decrypt(encrypted, TestKey);
        
        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void FishBase64Decode_HandlesPartialBlocks()
    {
        // Test that decode can handle strings not multiple of 12
        var shortString = "abc";
        
        var decoded = FishCryptService.FishBase64Decode(shortString);
        
        // Should pad to 12 characters internally
        decoded.Should().NotBeNull();
        decoded.Length.Should().Be(8); // One 8-byte block
    }
}
