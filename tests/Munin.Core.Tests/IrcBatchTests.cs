using FluentAssertions;
using Munin.Core.Models;
using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Tests for IrcBatch - IRCv3 batch message handling, nested batches, completion tracking
/// </summary>
public class IrcBatchTests
{
    [Fact]
    public void Constructor_InitializesWithDefaults()
    {
        // Act
        var batch = new IrcBatch();

        // Assert
        batch.Reference.Should().BeEmpty();
        batch.Type.Should().BeEmpty();
        batch.Parameters.Should().BeEmpty();
        batch.Messages.Should().BeEmpty();
        batch.IsComplete.Should().BeFalse();
        batch.ParentReference.Should().BeNull();
    }

    [Fact]
    public void Reference_CanBeSet()
    {
        // Arrange
        var batch = new IrcBatch();

        // Act
        batch.Reference = "yXNAbvnRHTRBv";

        // Assert
        batch.Reference.Should().Be("yXNAbvnRHTRBv");
    }

    [Fact]
    public void Type_CanBeSet()
    {
        // Arrange
        var batch = new IrcBatch();

        // Act
        batch.Type = "chathistory";

        // Assert
        batch.Type.Should().Be("chathistory");
    }

    [Fact]
    public void Parameters_CanBeAdded()
    {
        // Arrange
        var batch = new IrcBatch();

        // Act
        batch.Parameters.Add("#channel");
        batch.Parameters.Add("timestamp=2025-12-30T12:00:00Z");

        // Assert
        batch.Parameters.Should().HaveCount(2);
        batch.Parameters[0].Should().Be("#channel");
        batch.Parameters[1].Should().Be("timestamp=2025-12-30T12:00:00Z");
    }

    [Fact]
    public void Messages_CanBeAdded()
    {
        // Arrange
        var batch = new IrcBatch();
        var message1 = new ParsedIrcMessage { Command = "PRIVMSG" };
        var message2 = new ParsedIrcMessage { Command = "NOTICE" };

        // Act
        batch.Messages.Add(message1);
        batch.Messages.Add(message2);

        // Assert
        batch.Messages.Should().HaveCount(2);
        batch.Messages[0].Should().BeSameAs(message1);
        batch.Messages[1].Should().BeSameAs(message2);
    }

    [Fact]
    public void IsComplete_CanBeSetTrue()
    {
        // Arrange
        var batch = new IrcBatch();

        // Act
        batch.IsComplete = true;

        // Assert
        batch.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void ParentReference_CanBeSet()
    {
        // Arrange
        var batch = new IrcBatch();

        // Act
        batch.ParentReference = "parent_batch_ref";

        // Assert
        batch.ParentReference.Should().Be("parent_batch_ref");
    }

    [Fact]
    public void ChatHistoryBatch_CanBeCreated()
    {
        // Arrange & Act
        var batch = new IrcBatch
        {
            Reference = "hist123",
            Type = "chathistory",
            Parameters = new List<string> { "#linux" }
        };

        // Assert
        batch.Reference.Should().Be("hist123");
        batch.Type.Should().Be("chathistory");
        batch.Parameters.Should().ContainSingle().Which.Should().Be("#linux");
    }

    [Fact]
    public void NetjoinBatch_CanBeCreated()
    {
        // Arrange & Act
        var batch = new IrcBatch
        {
            Reference = "netjoin456",
            Type = "netjoin"
        };

        // Assert
        batch.Reference.Should().Be("netjoin456");
        batch.Type.Should().Be("netjoin");
    }

    [Fact]
    public void NetsplitBatch_CanBeCreated()
    {
        // Arrange & Act
        var batch = new IrcBatch
        {
            Reference = "netsplit789",
            Type = "netsplit",
            Parameters = new List<string> { "server1.example.com", "server2.example.com" }
        };

        // Assert
        batch.Reference.Should().Be("netsplit789");
        batch.Type.Should().Be("netsplit");
        batch.Parameters.Should().HaveCount(2);
        batch.Parameters[0].Should().Be("server1.example.com");
        batch.Parameters[1].Should().Be("server2.example.com");
    }

    [Fact]
    public void NestedBatch_CanReferenceParent()
    {
        // Arrange
        var parentBatch = new IrcBatch
        {
            Reference = "parent123",
            Type = "chathistory"
        };

        var childBatch = new IrcBatch
        {
            Reference = "child456",
            Type = "labeled-response",
            ParentReference = parentBatch.Reference
        };

        // Act & Assert
        childBatch.ParentReference.Should().Be("parent123");
        childBatch.ParentReference.Should().Be(parentBatch.Reference);
    }

    [Fact]
    public void Batch_AccumulatesMultipleMessages()
    {
        // Arrange
        var batch = new IrcBatch
        {
            Reference = "batch001",
            Type = "chathistory"
        };

        // Act
        for (int i = 0; i < 10; i++)
        {
            batch.Messages.Add(new ParsedIrcMessage
            {
                Command = "PRIVMSG",
                Parameters = new List<string> { "#channel", $"Message {i}" }
            });
        }

        // Assert
        batch.Messages.Should().HaveCount(10);
        batch.Messages[0].Parameters[1].Should().Be("Message 0");
        batch.Messages[9].Parameters[1].Should().Be("Message 9");
    }

    [Fact]
    public void CompletedBatch_HasAllProperties()
    {
        // Arrange & Act
        var batch = new IrcBatch
        {
            Reference = "complete123",
            Type = "chathistory",
            Parameters = new List<string> { "#test" },
            IsComplete = true
        };

        batch.Messages.Add(new ParsedIrcMessage { Command = "PRIVMSG" });
        batch.Messages.Add(new ParsedIrcMessage { Command = "PRIVMSG" });

        // Assert
        batch.Reference.Should().Be("complete123");
        batch.Type.Should().Be("chathistory");
        batch.Parameters.Should().ContainSingle();
        batch.Messages.Should().HaveCount(2);
        batch.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void EmptyBatch_CanBeCompleted()
    {
        // Arrange
        var batch = new IrcBatch
        {
            Reference = "empty001",
            Type = "netjoin"
        };

        // Act
        batch.IsComplete = true;

        // Assert
        batch.Messages.Should().BeEmpty();
        batch.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void Batch_SupportsLabeledResponse()
    {
        // Arrange & Act
        var batch = new IrcBatch
        {
            Reference = "label001",
            Type = "labeled-response",
            Parameters = new List<string> { "abc123" }
        };

        // Assert
        batch.Type.Should().Be("labeled-response");
        batch.Parameters.Should().ContainSingle().Which.Should().Be("abc123");
    }

    [Fact]
    public void MultipleBatches_HaveUniqueReferences()
    {
        // Arrange & Act
        var batch1 = new IrcBatch { Reference = "ref1" };
        var batch2 = new IrcBatch { Reference = "ref2" };
        var batch3 = new IrcBatch { Reference = "ref3" };

        // Assert
        batch1.Reference.Should().NotBe(batch2.Reference);
        batch2.Reference.Should().NotBe(batch3.Reference);
        batch1.Reference.Should().NotBe(batch3.Reference);
    }

    [Fact]
    public void Batch_CanHaveNoParameters()
    {
        // Arrange & Act
        var batch = new IrcBatch
        {
            Reference = "noparam",
            Type = "netjoin"
        };

        // Assert
        batch.Parameters.Should().BeEmpty();
    }

    [Fact]
    public void NestedBatch_CanBeCreatedInParent()
    {
        // Arrange
        var parentBatch = new IrcBatch
        {
            Reference = "parent",
            Type = "chathistory"
        };

        // Act - Simulate nested batch
        var nestedMessage = new ParsedIrcMessage
        {
            Command = "BATCH",
            Tags = new Dictionary<string, string> { { "batch", parentBatch.Reference } }
        };
        parentBatch.Messages.Add(nestedMessage);

        // Assert
        parentBatch.Messages.Should().ContainSingle();
        parentBatch.Messages[0].Command.Should().Be("BATCH");
        parentBatch.Messages[0].Tags.Should().ContainKey("batch");
    }

    [Fact]
    public void Batch_MessageOrder_IsPreserved()
    {
        // Arrange
        var batch = new IrcBatch { Reference = "order", Type = "chathistory" };

        // Act
        batch.Messages.Add(new ParsedIrcMessage { Command = "PRIVMSG", Parameters = new List<string> { "#chan", "First" } });
        batch.Messages.Add(new ParsedIrcMessage { Command = "PRIVMSG", Parameters = new List<string> { "#chan", "Second" } });
        batch.Messages.Add(new ParsedIrcMessage { Command = "PRIVMSG", Parameters = new List<string> { "#chan", "Third" } });

        // Assert
        batch.Messages[0].Parameters[1].Should().Be("First");
        batch.Messages[1].Parameters[1].Should().Be("Second");
        batch.Messages[2].Parameters[1].Should().Be("Third");
    }
}
