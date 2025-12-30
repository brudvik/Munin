using FluentAssertions;
using Munin.Core.Services;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for CertificatePinningService - SSL certificate pinning and change detection.
/// </summary>
public class CertificatePinningServiceTests : IDisposable
{
    private readonly string _tempPinsFile;

    public CertificatePinningServiceTests()
    {
        // Use a temp file for testing to avoid interfering with real pins
        _tempPinsFile = Path.Combine(Path.GetTempPath(), $"test_pins_{Guid.NewGuid()}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempPinsFile))
        {
            File.Delete(_tempPinsFile);
        }
    }

    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        // Act
        var instance1 = CertificatePinningService.Instance;
        var instance2 = CertificatePinningService.Instance;

        // Assert
        instance1.Should().BeSameAs(instance2);
    }

    [Fact]
    public void ValidateCertificate_NewServer_ShouldReturnNewCertificate()
    {
        // Arrange
        var service = CertificatePinningService.Instance;
        var cert = CreateSelfSignedCertificate("test.server.com");
        var testHost = $"test{Guid.NewGuid()}.example.com";

        try
        {
            // Act
            var result = service.ValidateCertificate(testHost, cert);

            // Assert
            result.Should().Be(CertificateValidationResult.NewCertificate);
        }
        finally
        {
            service.RemovePin(testHost);
            cert.Dispose();
        }
    }

    [Fact]
    public void ValidateCertificate_SameCertificate_ShouldReturnValid()
    {
        // Arrange
        var service = CertificatePinningService.Instance;
        var cert = CreateSelfSignedCertificate("test.server2.com");
        var testHost = $"test{Guid.NewGuid()}.example.com";

        try
        {
            // Pin the certificate first
            service.ValidateCertificate(testHost, cert);

            // Act
            var result = service.ValidateCertificate(testHost, cert);

            // Assert
            result.Should().Be(CertificateValidationResult.Valid);
        }
        finally
        {
            service.RemovePin(testHost);
            cert.Dispose();
        }
    }

    [Fact]
    public void ValidateCertificate_DifferentCertificate_ShouldReturnChanged()
    {
        // Arrange
        var service = CertificatePinningService.Instance;
        var cert1 = CreateSelfSignedCertificate("test.server3.com");
        var cert2 = CreateSelfSignedCertificate("different.server.com");
        var testHost = $"test{Guid.NewGuid()}.example.com";

        try
        {
            // Pin the first certificate
            service.ValidateCertificate(testHost, cert1);

            // Act - validate with different certificate
            var result = service.ValidateCertificate(testHost, cert2);

            // Assert
            result.Should().Be(CertificateValidationResult.Changed);
        }
        finally
        {
            service.RemovePin(testHost);
            cert1.Dispose();
            cert2.Dispose();
        }
    }

    [Fact]
    public void CertificateChanged_EventFired_WhenCertificateChanges()
    {
        // Arrange
        var service = CertificatePinningService.Instance;
        var cert1 = CreateSelfSignedCertificate("test.server4.com");
        var cert2 = CreateSelfSignedCertificate("different.server2.com");
        var testHost = $"test{Guid.NewGuid()}.example.com";
        
        CertificateChangedEventArgs? eventArgs = null;
        service.CertificateChanged += (sender, args) => eventArgs = args;

        try
        {
            // Pin the first certificate
            service.ValidateCertificate(testHost, cert1);

            // Act
            service.ValidateCertificate(testHost, cert2);

            // Assert
            eventArgs.Should().NotBeNull();
            eventArgs!.ServerHost.Should().Be(testHost);
            eventArgs.PreviousFingerprint.Should().NotBeNull();
            eventArgs.NewFingerprint.Should().NotBeNull();
            eventArgs.PreviousFingerprint.Should().NotBe(eventArgs.NewFingerprint);
        }
        finally
        {
            service.RemovePin(testHost);
            cert1.Dispose();
            cert2.Dispose();
        }
    }

    [Fact]
    public void NewCertificateSeen_EventFired_WhenFirstTimeSeeingServer()
    {
        // Arrange
        var service = CertificatePinningService.Instance;
        var cert = CreateSelfSignedCertificate("test.server5.com");
        var testHost = $"test{Guid.NewGuid()}.example.com";
        
        CertificateChangedEventArgs? eventArgs = null;
        service.NewCertificateSeen += (sender, args) => eventArgs = args;

        try
        {
            // Act
            service.ValidateCertificate(testHost, cert);

            // Assert
            eventArgs.Should().NotBeNull();
            eventArgs!.ServerHost.Should().Be(testHost);
            eventArgs.PreviousFingerprint.Should().BeNull();
            eventArgs.NewFingerprint.Should().NotBeNull();
        }
        finally
        {
            service.RemovePin(testHost);
            cert.Dispose();
        }
    }

    [Fact]
    public void UpdatePin_ShouldUpdateExistingPin()
    {
        // Arrange
        var service = CertificatePinningService.Instance;
        var cert1 = CreateSelfSignedCertificate("test.server6.com");
        var cert2 = CreateSelfSignedCertificate("updated.server.com");
        var testHost = $"test{Guid.NewGuid()}.example.com";

        try
        {
            // Pin the first certificate
            service.ValidateCertificate(testHost, cert1);

            // Act
            service.UpdatePin(testHost, cert2);

            // Assert - second certificate should now be valid
            var result = service.ValidateCertificate(testHost, cert2);
            result.Should().Be(CertificateValidationResult.Valid);
        }
        finally
        {
            service.RemovePin(testHost);
            cert1.Dispose();
            cert2.Dispose();
        }
    }

    [Fact]
    public void RemovePin_ShouldRemovePinnedCertificate()
    {
        // Arrange
        var service = CertificatePinningService.Instance;
        var cert = CreateSelfSignedCertificate("test.server7.com");
        var testHost = $"test{Guid.NewGuid()}.example.com";

        try
        {
            // Pin the certificate
            service.ValidateCertificate(testHost, cert);

            // Act
            service.RemovePin(testHost);

            // Assert - should be treated as new certificate again
            var result = service.ValidateCertificate(testHost, cert);
            result.Should().Be(CertificateValidationResult.NewCertificate);
        }
        finally
        {
            service.RemovePin(testHost);
            cert.Dispose();
        }
    }

    [Fact]
    public void GetPin_ExistingPin_ShouldReturnPinInfo()
    {
        // Arrange
        var service = CertificatePinningService.Instance;
        var cert = CreateSelfSignedCertificate("test.server8.com");
        var testHost = $"test{Guid.NewGuid()}.example.com";

        try
        {
            // Pin the certificate
            service.ValidateCertificate(testHost, cert);

            // Act
            var pin = service.GetPin(testHost);

            // Assert
            pin.Should().NotBeNull();
            pin!.ServerHost.Should().Be(testHost);
            pin.Fingerprint.Should().NotBeNullOrEmpty();
            pin.Subject.Should().Contain("test.server8.com");
        }
        finally
        {
            service.RemovePin(testHost);
            cert.Dispose();
        }
    }

    [Fact]
    public void GetPin_NonExistentPin_ShouldReturnNull()
    {
        // Arrange
        var service = CertificatePinningService.Instance;
        var testHost = $"nonexistent{Guid.NewGuid()}.example.com";

        // Act
        var pin = service.GetPin(testHost);

        // Assert
        pin.Should().BeNull();
    }

    [Fact]
    public void GetAllPins_ShouldReturnAllStoredPins()
    {
        // Arrange
        var service = CertificatePinningService.Instance;
        var cert = CreateSelfSignedCertificate("test.server9.com");
        var testHost = $"test{Guid.NewGuid()}.example.com";

        try
        {
            // Pin the certificate
            service.ValidateCertificate(testHost, cert);

            // Act
            var allPins = service.GetAllPins();

            // Assert
            allPins.Should().ContainKey(testHost);
        }
        finally
        {
            service.RemovePin(testHost);
            cert.Dispose();
        }
    }

    [Fact]
    public void CertificatePin_ShouldStoreAllMetadata()
    {
        // Arrange
        var service = CertificatePinningService.Instance;
        var cert = CreateSelfSignedCertificate("test.server10.com");
        var testHost = $"test{Guid.NewGuid()}.example.com";

        try
        {
            // Act
            service.ValidateCertificate(testHost, cert);
            var pin = service.GetPin(testHost);

            // Assert
            pin.Should().NotBeNull();
            pin!.ServerHost.Should().Be(testHost);
            pin.Fingerprint.Should().NotBeNullOrEmpty();
            pin.Subject.Should().NotBeNullOrEmpty();
            pin.Issuer.Should().NotBeNullOrEmpty();
            pin.FirstSeen.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
            pin.LastSeen.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
            pin.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
        }
        finally
        {
            service.RemovePin(testHost);
            cert.Dispose();
        }
    }

    [Fact]
    public void CertificateChangedEventArgs_ShouldContainAllDetails()
    {
        // Arrange
        var args = new CertificateChangedEventArgs(
            "test.server.com",
            "ABCD1234",
            "EFGH5678",
            DateTime.UtcNow.AddDays(-30),
            "CN=test.server.com",
            DateTime.UtcNow.AddYears(1)
        );

        // Assert
        args.ServerHost.Should().Be("test.server.com");
        args.PreviousFingerprint.Should().Be("ABCD1234");
        args.NewFingerprint.Should().Be("EFGH5678");
        args.Subject.Should().Be("CN=test.server.com");
        args.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public void CertificateValidationResult_ShouldHaveAllValues()
    {
        // Assert - Verify enum has all expected values
        var values = Enum.GetValues<CertificateValidationResult>();
        values.Should().Contain(CertificateValidationResult.Valid);
        values.Should().Contain(CertificateValidationResult.NewCertificate);
        values.Should().Contain(CertificateValidationResult.Changed);
    }

    /// <summary>
    /// Helper method to create a self-signed certificate for testing.
    /// </summary>
    private static X509Certificate2 CreateSelfSignedCertificate(string subjectName)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={subjectName}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        return certificate;
    }
}
