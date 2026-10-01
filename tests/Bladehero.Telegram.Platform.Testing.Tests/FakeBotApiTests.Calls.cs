using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class FakeBotApiTests
{
    [Fact]
    public async Task Mark_ThenCallsSince_ShouldHoldOnlyLaterCalls()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        await client.SendMessage(Chat, "before");
        var mark = api.Mark();

        // Act
        await client.SendMessage(Chat, "after");

        // Assert
        api.CallsSince(mark).Should().ContainSingle().Which.Parameters["text"]!
            .GetValue<string>()
            .Should()
            .Be("after");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public async Task CallsSince_WithAMarkOutOfRange_ShouldThrow(int mark)
    {
        // Arrange: one call so far.
        var api = ApiWithChats();
        await api.CreateClient().SendMessage(Chat, "hello");

        // Act
        var act = () => api.CallsSince(mark);

        // Assert
        act.Should()
            .Throw<ArgumentOutOfRangeException>()
            .WithParameterName("mark")
            .WithMessage("A mark is 0 to 1, the calls so far.*");
    }

    [Fact]
    public async Task Sent_ShouldGiveTheRequestsAsTelegramBotObjects()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();

        // Act
        await client.SendMessage(Chat, "Pick one", replyMarkup: InlineKeyboardButton.WithCallbackData("A", "pick:A"));

        // Assert
        var sent = api.Sent<SendMessageRequest>().Should().ContainSingle().Subject;
        using (new AssertionScope())
        {
            sent.Text.Should().Be("Pick one");
            sent.ReplyMarkup.Should()
                .BeOfType<InlineKeyboardMarkup>()
                .Which.InlineKeyboard.SelectMany(row => row)
                .Should()
                .ContainSingle()
                .Which.CallbackData.Should()
                .Be("pick:A");
        }
    }

    [Fact]
    public async Task Sent_ShouldIncludeRefusedRequestsAndLeaveOutGetUpdates()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        api.Fail("sendMessage", BotApiError.BotBlocked);

        // Act
        await Record.ExceptionAsync(() => client.SendMessage(Chat, "hello"));
        await client.GetUpdates(timeout: 0);

        // Assert
        api.Sent<IRequest>().Select(x => x.MethodName).Should().Equal("sendMessage");
    }

    [Fact]
    public async Task Sent_WhenTelegramBotRetriesAfterA429_ShouldHoldEachAttempt()
    {
        // Arrange: Telegram.Bot waits the 0 seconds asked and tries again.
        var api = ApiWithChats();
        var client = api.CreateClient();
        api.Fail("sendMessage", BotApiError.TooManyRequests(0), times: 1);

        // Act
        await client.SendMessage(Chat, "hello");

        // Assert
        using (new AssertionScope())
        {
            api.Sent<SendMessageRequest>().Select(x => x.Text).Should().Equal("hello", "hello");
            api.Calls.Select(x => x.Method).Should().Equal("sendMessage", "sendMessage");
        }
    }

    [Fact]
    public async Task WaitForCallAsync_WhenAMatchIsAlreadyThere_ShouldReturnItAtOnce()
    {
        // Arrange
        var api = ApiWithChats();
        await api.CreateClient().SendMessage(Chat, "hello");

        // Act
        var call = await api.WaitForCallAsync("SendMessage", timeout: TimeSpan.FromMilliseconds(1));

        // Assert
        call.Parameters["text"]!
            .GetValue<string>()
            .Should()
            .Be("hello");
    }

    [Fact]
    public async Task WaitForCallAsync_WithAfter_ShouldWaitForALaterCall()
    {
        // Arrange: /later answers from the background, after the update is handled.
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("hello");
        var mark = bot.Api.Mark();
        await nick.SendsAsync("/later");

        // Act
        var call = await bot.Api.WaitForCallAsync("sendMessage", after: mark);

        // Assert
        call.Parameters["text"]!
            .GetValue<string>()
            .Should()
            .Be("later");
    }

    [Fact]
    public async Task WaitForCallAsync_WhenNothingComes_ShouldTimeOutListingTheCalls()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        var mark = api.Mark();
        await client.GetMe();
        await client.SendMessage(Chat, "hi");

        // Act
        var act = () =>
            api.WaitForCallAsync(
                "sendMessage",
                x => x.Parameters["text"]!.GetValue<string>() == "bye",
                after: mark,
                timeout: TimeSpan.FromMilliseconds(50)
            );

        // Assert
        (await act.Should().ThrowAsync<TimeoutException>())
            .Which.Message.Should()
            .Be(
                "No sendMessage call matching the predicate came within 50 ms. The calls since then were: getMe, "
                    + "sendMessage."
            );
    }

    [Fact]
    public async Task WaitForCallAsync_WhenTheBotMadeNoCalls_ShouldSaySo()
    {
        // Arrange
        var api = ApiWithChats();

        // Act
        var act = () => api.WaitForCallAsync("answerCallbackQuery", timeout: TimeSpan.FromMilliseconds(50));

        // Assert
        (await act.Should().ThrowAsync<TimeoutException>())
            .Which.Message.Should()
            .Be("No answerCallbackQuery call came within 50 ms. The bot made no calls since then.");
    }

    [Fact]
    public async Task WaitForCallAsync_AfterManyCalls_ShouldListTheFirstTen()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        var mark = api.Mark();
        for (var call = 0; call < 11; call++)
        {
            await client.GetMe();
        }

        // Act
        var act = () => api.WaitForCallAsync("sendMessage", after: mark, timeout: TimeSpan.FromMilliseconds(50));

        // Assert
        (await act.Should().ThrowAsync<TimeoutException>())
            .Which.Message.Should()
            .Be(
                "No sendMessage call came within 50 ms. The calls since then were: "
                    + string.Join(", ", Enumerable.Repeat("getMe", 10))
                    + ", …."
            );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task WaitForCallAsync_WithANonPositiveTimeout_ShouldThrow(int milliseconds)
    {
        // Arrange
        var api = ApiWithChats();

        // Act
        var act = () => api.WaitForCallAsync("sendMessage", timeout: TimeSpan.FromMilliseconds(milliseconds));

        // Assert
        (await act.Should().ThrowAsync<ArgumentOutOfRangeException>())
            .WithParameterName("timeout")
            .Which.Message.Should()
            .StartWith("Waiting needs some time.");
    }
}
