using System.Numerics;
using FluentAssertions;
using Munin.Core.Services;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for Dh1080KeyExchange - DH1080 key exchange for FiSH encryption.
/// </summary>
public class Dh1080KeyExchangeTests
{
    [Fact]
    public void GeneratePublicKey_ReturnsNonEmptyString()
    {
        var dh = new Dh1080KeyExchange();
        
        var publicKey = dh.GeneratePublicKey();
        
        publicKey.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GeneratePublicKey_GeneratesUniqueKeys()
    {
        var dh1 = new Dh1080KeyExchange();
        var dh2 = new Dh1080KeyExchange();
        
        var key1 = dh1.GeneratePublicKey();
        var key2 = dh2.GeneratePublicKey();
        
        key1.Should().NotBe(key2);
    }

    [Fact]
    public void ComputeSharedSecret_WithoutGeneratingKey_ReturnsNull()
    {
        var dh = new Dh1080KeyExchange();
        var somePublicKey = new Dh1080KeyExchange().GeneratePublicKey();
        
        var secret = dh.ComputeSharedSecret(somePublicKey);
        
        secret.Should().BeNull();
    }

    [Fact]
    public void ComputeSharedSecret_WithValidKeys_ReturnsSharedSecret()
    {
        var alice = new Dh1080KeyExchange();
        var bob = new Dh1080KeyExchange();
        
        var alicePublic = alice.GeneratePublicKey();
        var bobPublic = bob.GeneratePublicKey();
        
        var aliceSecret = alice.ComputeSharedSecret(bobPublic);
        var bobSecret = bob.ComputeSharedSecret(alicePublic);
        
        aliceSecret.Should().NotBeNull();
        bobSecret.Should().NotBeNull();
        aliceSecret.Should().Be(bobSecret);
    }

    [Fact]
    public void ComputeSharedSecret_GeneratesSameSecretForBothParties()
    {
        // Full DH key exchange simulation
        var initiator = new Dh1080KeyExchange();
        var responder = new Dh1080KeyExchange();
        
        // Initiator generates and sends public key
        var initiatorPublic = initiator.GeneratePublicKey();
        
        // Responder generates public key and computes shared secret
        var responderPublic = responder.GeneratePublicKey();
        var responderSecret = responder.ComputeSharedSecret(initiatorPublic);
        
        // Initiator receives responder's public key and computes shared secret
        var initiatorSecret = initiator.ComputeSharedSecret(responderPublic);
        
        // Both should have the same shared secret
        initiatorSecret.Should().Be(responderSecret);
        initiatorSecret.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CreateInitMessage_DefaultCbc_ContainsCbcIndicator()
    {
        var dh = new Dh1080KeyExchange();
        
        var message = dh.CreateInitMessage();
        
        message.Should().StartWith("DH1080_INIT_cbc ");
    }

    [Fact]
    public void CreateInitMessage_WithEcb_DoesNotContainCbc()
    {
        var dh = new Dh1080KeyExchange();
        
        var message = dh.CreateInitMessage(useCbc: false);
        
        // Should start with DH1080_INIT (not DH1080_INIT_cbc)
        message.Should().StartWith("DH1080_INIT ");
        message.Should().NotStartWith("DH1080_INIT_cbc");
        // Should not end with CBC suffix (FiSH-irssi format)
        message.Should().NotEndWith(" CBC");
    }

    [Fact]
    public void CreateInitMessage_FishIrssiFormat_HasCbcSuffix()
    {
        var dh = new Dh1080KeyExchange();
        
        var message = dh.CreateInitMessage(useCbc: true, useMircFormat: false);
        
        message.Should().StartWith("DH1080_INIT ");
        message.Should().EndWith(" CBC");
    }

    [Fact]
    public void CreateFinishMessage_ReturnsMessageAndKey()
    {
        var initiator = new Dh1080KeyExchange();
        var responder = new Dh1080KeyExchange();
        
        var initPublicKey = initiator.GeneratePublicKey();
        
        var (message, sharedKey) = responder.CreateFinishMessage(initPublicKey);
        
        message.Should().StartWith("DH1080_FINISH ");
        sharedKey.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CreateFinishMessage_WithCbc_ContainsCbcIndicator()
    {
        var initiator = new Dh1080KeyExchange();
        var responder = new Dh1080KeyExchange();
        
        var initPublicKey = initiator.GeneratePublicKey();
        
        var (message, _) = responder.CreateFinishMessage(initPublicKey, useCbc: true, useMircFormat: true);
        
        message.Should().StartWith("DH1080_FINISH_cbc ");
    }

    [Fact]
    public void ParseMessage_WithMircInitCbc_ParsesCorrectly()
    {
        var message = "DH1080_INIT_cbc ABC123XYZ";
        
        var parsed = Dh1080KeyExchange.ParseMessage(message);
        
        parsed.Should().NotBeNull();
        parsed!.Value.Command.Should().Be("DH1080_INIT");
        parsed.Value.PublicKey.Should().Be("ABC123XYZ");
        parsed.Value.IsCbc.Should().BeTrue();
        parsed.Value.UseMircFormat.Should().BeTrue();
    }

    [Fact]
    public void ParseMessage_WithFishIrssiInitCbc_ParsesCorrectly()
    {
        var message = "DH1080_INIT ABC123XYZ CBC";
        
        var parsed = Dh1080KeyExchange.ParseMessage(message);
        
        parsed.Should().NotBeNull();
        parsed!.Value.Command.Should().Be("DH1080_INIT");
        parsed.Value.PublicKey.Should().Be("ABC123XYZ");
        parsed.Value.IsCbc.Should().BeTrue();
        parsed.Value.UseMircFormat.Should().BeFalse();
    }

    [Fact]
    public void ParseMessage_WithInitEcb_ParsesCorrectly()
    {
        var message = "DH1080_INIT ABC123XYZ";
        
        var parsed = Dh1080KeyExchange.ParseMessage(message);
        
        parsed.Should().NotBeNull();
        parsed!.Value.Command.Should().Be("DH1080_INIT");
        parsed.Value.PublicKey.Should().Be("ABC123XYZ");
        parsed.Value.IsCbc.Should().BeFalse();
    }

    [Fact]
    public void ParseMessage_WithFinish_ParsesCorrectly()
    {
        var message = "DH1080_FINISH XYZ789ABC";
        
        var parsed = Dh1080KeyExchange.ParseMessage(message);
        
        parsed.Should().NotBeNull();
        parsed!.Value.Command.Should().Be("DH1080_FINISH");
        parsed.Value.PublicKey.Should().Be("XYZ789ABC");
    }

    [Fact]
    public void ParseMessage_WithInvalidFormat_ReturnsNull()
    {
        var message = "INVALID_MESSAGE";
        
        var parsed = Dh1080KeyExchange.ParseMessage(message);
        
        parsed.Should().BeNull();
    }

    [Fact]
    public void ParseMessage_WithTooFewParts_ReturnsNull()
    {
        var message = "DH1080_INIT";
        
        var parsed = Dh1080KeyExchange.ParseMessage(message);
        
        parsed.Should().BeNull();
    }

    [Fact]
    public void Reset_ClearsKeyState()
    {
        var dh = new Dh1080KeyExchange();
        dh.GeneratePublicKey();
        
        dh.Reset();
        
        // After reset, should need to generate key again
        var somePublicKey = new Dh1080KeyExchange().GeneratePublicKey();
        var secret = dh.ComputeSharedSecret(somePublicKey);
        secret.Should().BeNull();
    }

    [Fact]
    public void FullKeyExchange_MircFormat_Succeeds()
    {
        var alice = new Dh1080KeyExchange();
        var bob = new Dh1080KeyExchange();
        
        // Alice initiates
        var initMessage = alice.CreateInitMessage(useCbc: true, useMircFormat: true);
        var parsed = Dh1080KeyExchange.ParseMessage(initMessage);
        
        // Bob receives init and responds
        var (finishMessage, bobSecret) = bob.CreateFinishMessage(parsed!.Value.PublicKey, useCbc: true, useMircFormat: true);
        var parsedFinish = Dh1080KeyExchange.ParseMessage(finishMessage);
        
        // Alice receives finish
        var aliceSecret = alice.ComputeSharedSecret(parsedFinish!.Value.PublicKey);
        
        aliceSecret.Should().Be(bobSecret);
    }

    [Fact]
    public void FullKeyExchange_FishIrssiFormat_Succeeds()
    {
        var alice = new Dh1080KeyExchange();
        var bob = new Dh1080KeyExchange();
        
        // Alice initiates
        var initMessage = alice.CreateInitMessage(useCbc: true, useMircFormat: false);
        var parsed = Dh1080KeyExchange.ParseMessage(initMessage);
        
        parsed.Should().NotBeNull();
        parsed!.Value.IsCbc.Should().BeTrue();
        parsed.Value.UseMircFormat.Should().BeFalse();
        
        // Bob receives init and responds
        var (finishMessage, bobSecret) = bob.CreateFinishMessage(parsed.Value.PublicKey, useCbc: true, useMircFormat: false);
        var parsedFinish = Dh1080KeyExchange.ParseMessage(finishMessage);
        
        parsedFinish.Should().NotBeNull();
        bobSecret.Should().NotBeNullOrEmpty($"Bob's secret should not be null. Alice pubkey: {parsed.Value.PublicKey}");
        
        // Alice receives finish
        var aliceSecret = alice.ComputeSharedSecret(parsedFinish!.Value.PublicKey);
        
        aliceSecret.Should().NotBeNull($"Alice secret should not be null. Bob pubkey: {parsedFinish.Value.PublicKey}");
        aliceSecret.Should().Be(bobSecret);
    }

    [Fact]
    public void FullKeyExchange_EcbMode_Succeeds()
    {
        var alice = new Dh1080KeyExchange();
        var bob = new Dh1080KeyExchange();
        
        // Alice generates and sends public key
        var alicePublic = alice.GeneratePublicKey();
        
        // Bob receives init and responds
        var bobPublic = bob.GeneratePublicKey();
        var bobSecret = bob.ComputeSharedSecret(alicePublic);
        
        // Alice receives bob's public key
        var aliceSecret = alice.ComputeSharedSecret(bobPublic);
        
        aliceSecret.Should().Be(bobSecret);
        aliceSecret.Should().NotBeNull();
    }

    [Fact]
    public void SharedSecret_IsDeterministic()
    {
        var alice = new Dh1080KeyExchange();
        var bob = new Dh1080KeyExchange();
        
        var alicePublic = alice.GeneratePublicKey();
        var bobPublic = bob.GeneratePublicKey();
        
        // Compute multiple times
        var secret1 = alice.ComputeSharedSecret(bobPublic);
        var secret2 = alice.ComputeSharedSecret(bobPublic);
        var secret3 = alice.ComputeSharedSecret(bobPublic);
        
        secret1.Should().Be(secret2);
        secret2.Should().Be(secret3);
    }

    [Fact]
    public void DifferentKeyPairs_ProduceDifferentSharedSecrets()
    {
        var alice1 = new Dh1080KeyExchange();
        var alice2 = new Dh1080KeyExchange();
        var bob = new Dh1080KeyExchange();
        
        var alice1Public = alice1.GeneratePublicKey();
        var alice2Public = alice2.GeneratePublicKey();
        var bobPublic = bob.GeneratePublicKey();
        
        var secret1 = alice1.ComputeSharedSecret(bobPublic);
        var secret2 = alice2.ComputeSharedSecret(bobPublic);
        
        secret1.Should().NotBe(secret2);
    }

    [Fact]
    public void PublicKey_IsBase64Like()
    {
        var dh = new Dh1080KeyExchange();
        var publicKey = dh.GeneratePublicKey();
        
        // Public key should be alphanumeric with possible +, /, =, or A suffix
        publicKey.Should().MatchRegex("^[A-Za-z0-9+/=A]+$");
    }

    [Fact]
    public void SharedSecret_HasConsistentLength()
    {
        var alice = new Dh1080KeyExchange();
        var bob = new Dh1080KeyExchange();
        
        var alicePublic = alice.GeneratePublicKey();
        var bobPublic = bob.GeneratePublicKey();
        
        var aliceSecret = alice.ComputeSharedSecret(bobPublic);
        var bobSecret = bob.ComputeSharedSecret(alicePublic);
        
        // Shared secret should be base64-encoded SHA256 hash
        aliceSecret!.Length.Should().BeGreaterThan(40);
        bobSecret!.Length.Should().BeGreaterThan(40);
    }

    [Fact]
    public void ComputeSharedSecret_WithInvalidKey_ReturnsNull()
    {
        var dh = new Dh1080KeyExchange();
        dh.GeneratePublicKey();
        
        var secret = dh.ComputeSharedSecret("invalid_base64_!@#$");
        
        secret.Should().BeNull();
    }

    [Fact]
    public void MultipleExchanges_ProduceUniqueSecrets()
    {
        var secrets = new HashSet<string>();
        
        for (int i = 0; i < 5; i++)
        {
            var alice = new Dh1080KeyExchange();
            var bob = new Dh1080KeyExchange();
            
            var alicePublic = alice.GeneratePublicKey();
            var bobPublic = bob.GeneratePublicKey();
            
            var secret = alice.ComputeSharedSecret(bobPublic);
            secrets.Add(secret!);
        }
        
        // All 5 exchanges should produce unique secrets
        secrets.Should().HaveCount(5);
    }

    [Fact]
    public void ParseMessage_WithCaseInsensitiveCbc_ParsesCorrectly()
    {
        var messageLower = "DH1080_INIT_cbc ABC123";
        var messageUpper = "DH1080_INIT_CBC ABC123";
        
        var parsedLower = Dh1080KeyExchange.ParseMessage(messageLower);
        var parsedUpper = Dh1080KeyExchange.ParseMessage(messageUpper);
        
        parsedLower.Should().NotBeNull();
        parsedUpper.Should().NotBeNull();
        parsedLower!.Value.IsCbc.Should().BeTrue();
        parsedUpper!.Value.IsCbc.Should().BeTrue();
    }

    [Fact]
    public void CreateInitMessage_GeneratesPublicKey()
    {
        var dh = new Dh1080KeyExchange();
        
        var message = dh.CreateInitMessage();
        
        // Message should contain a public key
        message.Split(' ').Should().HaveCountGreaterThan(1);
        
        // Should be able to compute shared secret after init
        var other = new Dh1080KeyExchange();
        var otherPublic = other.GeneratePublicKey();
        var secret = dh.ComputeSharedSecret(otherPublic);
        secret.Should().NotBeNull();
    }

    [Fact]
    public void CreateFinishMessage_GeneratesNewPublicKey()
    {
        var alice = new Dh1080KeyExchange();
        var bob1 = new Dh1080KeyExchange();
        var bob2 = new Dh1080KeyExchange();
        
        var alicePublic = alice.GeneratePublicKey();
        
        var (message1, _) = bob1.CreateFinishMessage(alicePublic);
        var (message2, _) = bob2.CreateFinishMessage(alicePublic);
        
        // Different responders should generate different finish messages
        message1.Should().NotBe(message2);
    }
}
