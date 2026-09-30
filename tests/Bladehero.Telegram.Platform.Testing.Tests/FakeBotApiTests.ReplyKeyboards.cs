using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class FakeBotApiTests
{
    [Fact]
    public async Task SendMessage_WithAReplyKeyboard_ShouldNotShowItOnTheMessage()
    {
        // Arrange
        var api = ApiWithChats();

        // Act
        var sent = await api.CreateClient()
            .SendMessage(
                Chat,
                "Pick a drink",
                replyMarkup: new ReplyKeyboardMarkup([
                    ["Tea"],
                ])
            );

        // Assert
        using (new AssertionScope())
        {
            sent.ReplyMarkup.Should().BeNull();
            api.MessagesIn(Chat).Single().ContainsKey("reply_markup").Should().BeFalse();
        }
    }

    [Theory]
    [InlineData("keyboard")]
    [InlineData("force reply")]
    [InlineData("removal")]
    public async Task EditMessageText_WithAReplyKeyboard_ShouldBeRefused(string markup)
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        var sent = await client.SendMessage(Chat, "Pick a drink");
        var raw = new RawEditMessageTextRequest(
            Chat,
            sent.Id,
            markup switch
            {
                "keyboard" => """{"keyboard":[["Tea"]]}""",
                "force reply" => """{"force_reply":true}""",
                _ => """{"remove_keyboard":true}""",
            }
        );

        // Act
        var act = () => client.SendRequest(raw);

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Be("Bad Request: inline keyboard expected");
    }

    // Telegram.Bot's EditMessageText takes inline keyboards only.
    private sealed class RawEditMessageTextRequest(long chatId, int messageId, string markup)
        : RequestBase<Message>("editMessageText")
    {
        public override HttpContent ToHttpContent() =>
            JsonContent.Create(
                new
                {
                    chat_id = chatId,
                    message_id = messageId,
                    text = "Pick a tea",
                    reply_markup = JsonNode.Parse(markup),
                }
            );
    }
}
