using System.Text.Json.Serialization;

namespace Munin.Core.Services;

/// <summary>
/// JSON source generation context for AOT-compatible serialization.
/// This eliminates IL2026 warnings by using compile-time code generation
/// instead of reflection-based serialization.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(EncryptionMetadata))]
[JsonSerializable(typeof(SecurityAuditLog))]
[JsonSerializable(typeof(SecurityEvent))]
[JsonSerializable(typeof(List<SecurityEvent>))]
[JsonSerializable(typeof(ClientConfiguration))]
[JsonSerializable(typeof(ServerConfiguration))]
[JsonSerializable(typeof(ProxyConfiguration))]
[JsonSerializable(typeof(GeneralSettings))]
[JsonSerializable(typeof(List<ServerConfiguration>))]
[JsonSerializable(typeof(PrivacyMapping))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, List<string>>))]
[JsonSerializable(typeof(Dictionary<string, Dictionary<string, List<string>>>))]
[JsonSerializable(typeof(CertificatePin))]
[JsonSerializable(typeof(List<CertificatePin>))]
internal partial class JsonSourceGenerationContext : JsonSerializerContext
{
}
