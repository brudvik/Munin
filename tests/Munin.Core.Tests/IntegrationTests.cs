using FluentAssertions;
using Munin.Core.Services;
using System.Text.Json;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Integration tests for Munin.Core - testing multiple components working together.
/// </summary>
public class IntegrationTests : IDisposable
{
    private readonly string _testBasePath;

    public IntegrationTests()
    {
        _testBasePath = Path.Combine(Path.GetTempPath(), "MuninIntegrationTests_" + Guid.NewGuid());
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

    #region End-to-End Encryption Tests

    [Fact]
    public void EndToEndEncryption_FullWorkflow_WithPasswordChange()
    {
        // Setup: Create storage and enable encryption
        var storage = new SecureStorageService(_testBasePath);
        var initialPassword = "InitialPass123";
        var newPassword = "NewPass456";

        // Step 1: Enable encryption and save data
        storage.EnableEncryptionAsync(initialPassword).Wait();
        storage.WriteTextSync("test.txt", "Secret data");
        storage.WriteTextSync("nested/file.txt", "More secrets");

        // Verify files are encrypted on disk
        var rawBytes = File.ReadAllBytes(storage.GetFullPath("test.txt"));
        EncryptionService.IsEncrypted(rawBytes).Should().BeTrue();

        // Step 2: Lock and unlock with correct password
        storage.Lock();
        storage.IsUnlocked.Should().BeFalse();
        storage.Unlock(initialPassword).Should().BeTrue();

        // Step 3: Change password
        storage.ChangePasswordAsync(initialPassword, newPassword).Wait();

        // Step 4: Verify old password no longer works
        storage.Lock();
        storage.Unlock(initialPassword).Should().BeFalse();

        // Step 5: Verify new password works and data is intact
        storage.Unlock(newPassword).Should().BeTrue();
        storage.ReadTextSync("test.txt").Should().Be("Secret data");
        storage.ReadTextSync("nested/file.txt").Should().Be("More secrets");
    }

    [Fact]
    public void EndToEndEncryption_MultipleFiles_PersistsAcrossInstances()
    {
        var password = "TestPass123";

        // Instance 1: Create and encrypt
        var storage1 = new SecureStorageService(_testBasePath);
        storage1.EnableEncryptionAsync(password).Wait();
        storage1.WriteTextSync("file1.txt", "Data 1");
        storage1.WriteTextSync("file2.txt", "Data 2");
        storage1.WriteTextSync("dir/file3.txt", "Data 3");

        // Instance 2: Open and verify
        var storage2 = new SecureStorageService(_testBasePath);
        storage2.IsEncryptionEnabled.Should().BeTrue();
        storage2.Unlock(password).Should().BeTrue();
        
        storage2.ReadTextSync("file1.txt").Should().Be("Data 1");
        storage2.ReadTextSync("file2.txt").Should().Be("Data 2");
        storage2.ReadTextSync("dir/file3.txt").Should().Be("Data 3");
    }

    [Fact]
    public void EndToEndEncryption_DisableEncryption_DecryptsAllFiles()
    {
        var password = "TestPass123";
        var storage = new SecureStorageService(_testBasePath);

        // Enable encryption and write files
        storage.EnableEncryptionAsync(password).Wait();
        storage.WriteTextSync("file1.txt", "Content 1");
        storage.WriteTextSync("file2.txt", "Content 2");

        // Verify encrypted
        var bytes1 = File.ReadAllBytes(storage.GetFullPath("file1.txt"));
        EncryptionService.IsEncrypted(bytes1).Should().BeTrue();

        // Disable encryption
        storage.DisableEncryptionAsync(password).Wait();

        // Verify files are now plaintext
        var bytes2 = File.ReadAllBytes(storage.GetFullPath("file1.txt"));
        EncryptionService.IsEncrypted(bytes2).Should().BeFalse();

        // Verify content is still correct
        File.ReadAllText(storage.GetFullPath("file1.txt")).Should().Be("Content 1");
        File.ReadAllText(storage.GetFullPath("file2.txt")).Should().Be("Content 2");
    }

    #endregion

    #region FiSH + DH1080 Integration Tests

    [Fact]
    public void FishAndDh1080_CompleteKeyExchange_ThenEncryptMessages()
    {
        var fishService = new FishCryptService();
        
        // Simulate DH1080 key exchange between Alice and Bob
        var alice = new Dh1080KeyExchange();
        var bob = new Dh1080KeyExchange();
        
        // Alice initiates
        var alicePublic = alice.GeneratePublicKey();
        
        // Bob responds and computes shared secret
        var bobPublic = bob.GeneratePublicKey();
        var bobSecret = bob.ComputeSharedSecret(alicePublic);
        
        // Alice computes shared secret
        var aliceSecret = alice.ComputeSharedSecret(bobPublic);
        
        // Both should have same secret
        aliceSecret.Should().Be(bobSecret);
        
        // Set FiSH keys using the shared secret
        fishService.SetKey("server1", "#channel", aliceSecret!);
        
        // Alice encrypts a message
        var plaintext = "Secret message via DH1080!";
        var encrypted = fishService.Encrypt("server1", "#channel", plaintext);
        
        // Bob decrypts with same key
        var decrypted = fishService.Decrypt("server1", "#channel", encrypted!);
        
        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void FishAndDh1080_MultiplChannels_IndependentKeys()
    {
        var fishAlice = new FishCryptService();
        var fishBob = new FishCryptService();
        
        // Channel 1: Key exchange
        var dh1Alice = new Dh1080KeyExchange();
        var dh1Bob = new Dh1080KeyExchange();
        var pub1Alice = dh1Alice.GeneratePublicKey();
        var pub1Bob = dh1Bob.GeneratePublicKey();
        var secret1 = dh1Alice.ComputeSharedSecret(pub1Bob);
        
        // Channel 2: Different key exchange
        var dh2Alice = new Dh1080KeyExchange();
        var dh2Bob = new Dh1080KeyExchange();
        var pub2Alice = dh2Alice.GeneratePublicKey();
        var pub2Bob = dh2Bob.GeneratePublicKey();
        var secret2 = dh2Alice.ComputeSharedSecret(pub2Bob);
        
        // Set keys for different channels
        fishAlice.SetKey("server", "#channel1", secret1!);
        fishAlice.SetKey("server", "#channel2", secret2!);
        fishBob.SetKey("server", "#channel1", secret1!);
        fishBob.SetKey("server", "#channel2", secret2!);
        
        // Messages encrypted with different keys
        var msg1 = fishAlice.Encrypt("server", "#channel1", "Message for channel 1");
        var msg2 = fishAlice.Encrypt("server", "#channel2", "Message for channel 2");
        
        // Each channel decrypts only its own messages
        fishBob.Decrypt("server", "#channel1", msg1!).Should().Be("Message for channel 1");
        fishBob.Decrypt("server", "#channel2", msg2!).Should().Be("Message for channel 2");
        
        // Wrong channel can't decrypt correctly (returns garbage or null)
        var wrongDecrypt = fishBob.Decrypt("server", "#channel1", msg2!);
        wrongDecrypt.Should().NotBe("Message for channel 2"); // Should NOT decrypt correctly
    }

    [Fact]
    public void FishAndDh1080_FullProtocolFlow_WithCbcMode()
    {
        var fishInitiator = new FishCryptService();
        var fishResponder = new FishCryptService();
        
        var initiator = new Dh1080KeyExchange();
        var responder = new Dh1080KeyExchange();
        
        // Step 1: Initiator sends DH1080_INIT
        var initMessage = initiator.CreateInitMessage(useCbc: true);
        initMessage.Should().Contain("DH1080_INIT");
        initMessage.Should().Contain("cbc");
        
        // Step 2: Parse init message
        var parsed = Dh1080KeyExchange.ParseMessage(initMessage);
        parsed.Should().NotBeNull();
        parsed!.Value.IsCbc.Should().BeTrue();
        
        // Step 3: Responder sends DH1080_FINISH
        var (finishMessage, responderKey) = responder.CreateFinishMessage(
            parsed.Value.PublicKey, 
            useCbc: true, 
            useMircFormat: true
        );
        
        // Step 4: Parse finish message
        var parsedFinish = Dh1080KeyExchange.ParseMessage(finishMessage);
        var initiatorKey = initiator.ComputeSharedSecret(parsedFinish!.Value.PublicKey);
        
        // Keys should match
        initiatorKey.Should().Be(responderKey);
        
        // Step 5: Use keys with CBC mode FiSH
        fishInitiator.SetKey("irc.example.com", "#secret", "cbc:" + initiatorKey);
        fishResponder.SetKey("irc.example.com", "#secret", "cbc:" + responderKey);
        
        // Step 6: Exchange encrypted messages
        var message1 = "Hello from initiator!";
        var encrypted1 = fishInitiator.Encrypt("irc.example.com", "#secret", message1);
        var decrypted1 = fishResponder.Decrypt("irc.example.com", "#secret", encrypted1!);
        decrypted1.Should().Be(message1);
        
        var message2 = "Reply from responder!";
        var encrypted2 = fishResponder.Encrypt("irc.example.com", "#secret", message2);
        var decrypted2 = fishInitiator.Decrypt("irc.example.com", "#secret", encrypted2!);
        decrypted2.Should().Be(message2);
    }

    #endregion

    #region SCRAM Authentication Full Handshake Tests

    [Fact]
    public void ScramAuthentication_FullHandshake_Succeeds()
    {
        // Simulate a complete SCRAM-SHA-256 authentication between client and server
        var username = "alice";
        var password = "secret123";
        
        var client = new ScramAuthenticator(username, password);
        
        // Step 1: Client sends first message
        var clientFirst = client.GetClientFirstMessage();
        clientFirst.Should().StartWith("n,,n=alice,r=");
        
        // Step 2: Extract client nonce for server simulation
        var clientNonce = ExtractNonce(clientFirst, "r=");
        
        // Step 3: Server generates response (simulated)
        var salt = new byte[16];
        for (int i = 0; i < 16; i++) salt[i] = (byte)i;
        var saltBase64 = Convert.ToBase64String(salt);
        var serverNonce = clientNonce + "servernonce123";
        var iterations = 4096;
        var serverFirst = $"r={serverNonce},s={saltBase64},i={iterations}";
        
        // Step 4: Client processes server response and sends final message
        var clientFinal = client.ProcessServerFirstMessage(serverFirst);
        clientFinal.Should().Contain("c=biws"); // channel binding
        clientFinal.Should().Contain($"r={serverNonce}");
        clientFinal.Should().Contain(",p="); // client proof
        
        // Step 5: Server verifies and sends final message (simplified simulation)
        // In real scenario, server would verify client proof and send verifier
        // For this test, we just verify the client reached the correct state
        client.State.Should().Be(ScramAuthenticatorState.WaitingForServerFinal);
    }

    [Fact]
    public void ScramAuthentication_WithSpecialCharacters_HandlesCorrectly()
    {
        var username = "user=with,special";
        var password = "p@ssw0rd!";
        
        var client = new ScramAuthenticator(username, password);
        var clientFirst = client.GetClientFirstMessage();
        
        // Username should be sanitized: = → =3D, , → =2C
        clientFirst.Should().Contain("n=user=3Dwith=2Cspecial");
        
        // Should still complete authentication flow
        var clientNonce = ExtractNonce(clientFirst, "r=");
        var salt = Convert.ToBase64String(new byte[16]);
        var serverFirst = $"r={clientNonce}srv,s={salt},i=4096";
        
        var clientFinal = client.ProcessServerFirstMessage(serverFirst);
        clientFinal.Should().NotBeNull();
    }

    #endregion

    #region IRC Message Processing Flow Tests

    [Fact]
    public void IrcMessageFlow_ParsePrivmsgWithEncryption_DecryptsCorrectly()
    {
        var parser = new IrcMessageParser();
        var fishService = new FishCryptService();
        
        // Setup encryption key
        var serverId = "irc.example.com";
        var channel = "#secret";
        var key = "secretkey123";
        fishService.SetKey(serverId, channel, key);
        
        // Create encrypted message
        var plaintext = "This is a secret message!";
        var encrypted = FishCryptService.Encrypt(plaintext, key);
        
        // Simulate IRC PRIVMSG with encrypted content
        var ircMessage = $":alice!user@host PRIVMSG {channel} :{encrypted}";
        
        // Parse IRC message
        var parsed = parser.Parse(ircMessage);
        parsed.Command.Should().Be("PRIVMSG");
        parsed.Nick.Should().Be("alice");
        parsed.Parameters[0].Should().Be(channel);
        
        // Check if message is encrypted and decrypt
        var content = parsed.Trailing!;
        if (FishCryptService.IsEncrypted(content))
        {
            var decrypted = fishService.Decrypt(serverId, channel, content);
            decrypted.Should().Be(plaintext);
        }
    }

    [Fact]
    public void IrcMessageFlow_ParseCtcpDh1080_InitiatesKeyExchange()
    {
        var parser = new IrcMessageParser();
        var dh = new Dh1080KeyExchange();
        
        // Create DH1080_INIT message
        var initMessage = dh.CreateInitMessage();
        var ctcpMessage = $"\x01{initMessage}\x01";
        
        // Simulate IRC PRIVMSG with CTCP
        var ircMessage = $":bob!user@host PRIVMSG alice :{ctcpMessage}";
        
        // Parse IRC message
        var parsed = parser.Parse(ircMessage);
        parsed.Command.Should().Be("PRIVMSG");
        parsed.Nick.Should().Be("bob");
        
        // Check if it's a CTCP message
        var content = parsed.Trailing!;
        IrcMessageParser.IsCTCP(content).Should().BeTrue();
        
        // Parse CTCP - get full message content without CTCP delimiters
        var ctcpContent = content.Trim('\x01');
        
        // Parse DH1080 message (returns tuple with Command, PublicKey, IsCbc, UseMircFormat)
        var dhParsed = Dh1080KeyExchange.ParseMessage(ctcpContent);
        dhParsed.Should().NotBeNull();
        dhParsed!.Value.Command.Should().Be("DH1080_INIT");
        dhParsed.Value.PublicKey.Should().NotBeEmpty();
        dhParsed.Value.IsCbc.Should().BeTrue();
    }

    [Fact]
    public void IrcMessageFlow_MultipleMessages_ProcessInOrder()
    {
        var parser = new IrcMessageParser();
        var messages = new[]
        {
            ":server 001 alice :Welcome to the IRC Network",
            ":alice!user@host JOIN #channel",
            ":bob!user@host PRIVMSG #channel :Hello everyone!",
            ":alice!user@host PRIVMSG #channel :Hi Bob!",
            ":charlie!user@host PART #channel :Goodbye"
        };
        
        var results = messages.Select(m => parser.Parse(m)).ToList();
        
        results[0].Command.Should().Be("001"); // Welcome
        results[1].Command.Should().Be("JOIN");
        results[1].Nick.Should().Be("alice");
        
        results[2].Command.Should().Be("PRIVMSG");
        results[2].Nick.Should().Be("bob");
        results[2].Trailing.Should().Be("Hello everyone!");
        
        results[3].Nick.Should().Be("alice");
        results[3].Trailing.Should().Be("Hi Bob!");
        
        results[4].Command.Should().Be("PART");
        results[4].Nick.Should().Be("charlie");
    }

    #endregion

    #region Configuration Persistence Tests

    [Fact]
    public void ConfigurationPersistence_SaveLoadWithEncryption()
    {
        var storage = new SecureStorageService(_testBasePath);
        var password = "ConfigPass123";
        
        // Enable encryption
        storage.EnableEncryptionAsync(password).Wait();
        
        // Create configuration
        var config = new ClientConfiguration
        {
            Settings = new GeneralSettings
            {
                ReconnectOnDisconnect = true,
                ReconnectDelaySeconds = 10
            }
        };
        
        // Save encrypted
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        storage.WriteTextSync("config.json", json);
        
        // Verify encrypted on disk
        var rawBytes = File.ReadAllBytes(storage.GetFullPath("config.json"));
        EncryptionService.IsEncrypted(rawBytes).Should().BeTrue();
        
        // Load and verify
        var loaded = storage.ReadTextSync("config.json");
        var loadedConfig = JsonSerializer.Deserialize<ClientConfiguration>(loaded!);
        
        loadedConfig.Should().NotBeNull();
        loadedConfig!.Settings.ReconnectOnDisconnect.Should().BeTrue();
        loadedConfig.Settings.ReconnectDelaySeconds.Should().Be(10);
    }

    [Fact]
    public void ConfigurationPersistence_MultipleConfigs_IndependentFiles()
    {
        var storage = new SecureStorageService(_testBasePath);
        var password = "Pass123";
        
        storage.EnableEncryptionAsync(password).Wait();
        
        // Save multiple configurations
        storage.WriteTextSync("servers.json", JsonSerializer.Serialize(new { Server = "irc.example.com" }));
        storage.WriteTextSync("settings.json", JsonSerializer.Serialize(new { Theme = "Dark" }));
        storage.WriteTextSync("keys.json", JsonSerializer.Serialize(new { Key1 = "value1" }));
        
        // Reload and verify each is independent
        var servers = storage.ReadTextSync("servers.json");
        var settings = storage.ReadTextSync("settings.json");
        var keys = storage.ReadTextSync("keys.json");
        
        servers.Should().Contain("irc.example.com");
        settings.Should().Contain("Dark");
        keys.Should().Contain("value1");
    }

    #endregion

    #region Helper Methods

    private static string ExtractNonce(string message, string prefix)
    {
        var parts = message.Split(',');
        foreach (var part in parts)
        {
            if (part.StartsWith(prefix))
            {
                return part.Substring(prefix.Length);
            }
        }
        return string.Empty;
    }

    #endregion
}
