using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Tests.History;

public sealed class TelegramHistoryTests
{
    [Fact]
    public async Task ReadAsync_ShouldIncludeEntriesRecordedBeforeIt()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync();
        host.Store.Close();
        host.Writer.Record(HistoryHost.Entry(1));
        var read = host.History.ReadAsync(new() { ChatId = HistoryHost.ChatId });
        await host.Store.Entered;
        var whileStoring = read.IsCompleted;

        // Act
        host.Store.Open();
        var entries = await read;

        // Assert
        using (new AssertionScope())
        {
            whileStoring.Should().BeFalse();
            entries.Select(x => x.Text).Should().Equal("#1");
        }
    }

    [Fact]
    public async Task ReadAsync_WithAMessageIdButNoChat_ShouldThrowArgumentException()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync();

        // Act
        var act = () => host.History.ReadAsync(new() { MessageId = 42 });

        // Assert
        await act.Should()
            .ThrowExactlyAsync<ArgumentException>()
            .WithMessage("A message id is unique only within its chat; set ChatId too.*");
    }

    [Fact]
    public async Task ReadAsync_WithALimitBelowOne_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync();

        // Act
        var act = () => host.History.ReadAsync(new() { ChatId = HistoryHost.ChatId, Limit = 0 });

        // Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("query");
    }

    [Fact]
    public async Task FlushAsync_ShouldWaitForTheWriter()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync();
        host.Store.Close();
        host.Writer.Record(HistoryHost.Entry(1));
        var flush = host.History.FlushAsync();
        await host.Store.Entered;
        var whileStoring = flush.IsCompleted;

        // Act
        host.Store.Open();
        await flush;

        // Assert
        using (new AssertionScope())
        {
            whileStoring.Should().BeFalse();
            host.Store.Entries.Select(x => x.Text).Should().Equal("#1");
        }
    }
}
