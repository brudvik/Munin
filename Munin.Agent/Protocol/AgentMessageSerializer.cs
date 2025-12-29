using System.Buffers.Binary;
using System.Net.Security;
using System.Text;

namespace Munin.Agent.Protocol;

/// <summary>
/// Handles serialization and deserialization of Agent protocol messages.
/// Message format: [Magic 4][Version 1][Type 1][Sequence 4][Length 4][Payload N]
/// </summary>
public static class AgentMessageSerializer
{
    private const int HeaderSize = 14; // 4 + 1 + 1 + 4 + 4

    /// <summary>
    /// Serializes a message to bytes.
    /// </summary>
    public static byte[] Serialize(AgentMessage message)
    {
        var totalSize = HeaderSize + message.Payload.Length;
        var buffer = new byte[totalSize];
        var offset = 0;

        // Magic bytes
        AgentProtocol.MagicBytes.CopyTo(buffer, offset);
        offset += 4;

        // Version
        buffer[offset++] = (byte)AgentProtocol.Version;

        // Message type
        buffer[offset++] = (byte)message.Type;

        // Sequence number
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset), message.SequenceNumber);
        offset += 4;

        // Payload length
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset), (uint)message.Payload.Length);
        offset += 4;

        // Payload
        if (message.Payload.Length > 0)
        {
            message.Payload.CopyTo(buffer, offset);
        }

        return buffer;
    }

    /// <summary>
    /// Writes a message to a stream.
    /// </summary>
    public static async Task WriteAsync(Stream stream, AgentMessage message, CancellationToken ct = default)
    {
        var data = Serialize(message);
        await stream.WriteAsync(data, ct);
        await stream.FlushAsync(ct);
    }

    /// <summary>
    /// Reads a message from a stream.
    /// </summary>
    public static async Task<AgentMessage?> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        // Read header
        var header = new byte[HeaderSize];
        var bytesRead = await ReadExactlyAsync(stream, header, ct);
        
        if (bytesRead < HeaderSize)
            return null;

        var offset = 0;

        // Verify magic bytes
        for (int i = 0; i < AgentProtocol.MagicBytes.Length; i++)
        {
            if (header[offset + i] != AgentProtocol.MagicBytes[i])
                throw new ProtocolViolationException("Invalid magic bytes");
        }
        offset += 4;

        // Check version
        var version = header[offset++];
        if (version != AgentProtocol.Version)
            throw new ProtocolViolationException($"Unsupported protocol version: {version}");

        // Message type
        var messageType = (AgentMessageType)header[offset++];

        // Sequence number
        var sequenceNumber = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(offset));
        offset += 4;

        // Payload length
        var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(offset));

        if (payloadLength > AgentProtocol.MaxMessageSize)
            throw new ProtocolViolationException($"Payload too large: {payloadLength}");

        // Read payload
        var payload = Array.Empty<byte>();
        if (payloadLength > 0)
        {
            payload = new byte[payloadLength];
            bytesRead = await ReadExactlyAsync(stream, payload, ct);
            
            if (bytesRead < payloadLength)
                return null;
        }

        return new AgentMessage
        {
            Type = messageType,
            SequenceNumber = sequenceNumber,
            Payload = payload
        };
    }

    /// <summary>
    /// Reads exactly the specified number of bytes, handling partial reads.
    /// </summary>
    private static async Task<int> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead), ct);
            if (read == 0)
                break; // End of stream
            totalRead += read;
        }
        return totalRead;
    }

    /// <summary>
    /// Creates an error response message.
    /// </summary>
    public static AgentMessage CreateError(string message, uint sequenceNumber = 0)
    {
        return new AgentMessage
        {
            Type = AgentMessageType.Error,
            Payload = Encoding.UTF8.GetBytes(message),
            SequenceNumber = sequenceNumber
        };
    }

    /// <summary>
    /// Creates a success response message.
    /// </summary>
    public static AgentMessage CreateSuccess(string? message = null, uint sequenceNumber = 0)
    {
        return new AgentMessage
        {
            Type = AgentMessageType.Success,
            Payload = message != null ? Encoding.UTF8.GetBytes(message) : Array.Empty<byte>(),
            SequenceNumber = sequenceNumber
        };
    }
}

/// <summary>
/// Exception thrown when protocol violations are detected.
/// </summary>
public class ProtocolViolationException : Exception
{
    public ProtocolViolationException(string message) : base(message) { }
}
