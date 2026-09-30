using System.Text;
using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class FakeBotApiTests
{
    [Fact]
    public async Task StoreUserPhoto_ShouldGiveSizesLikeASentPhoto()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var sent = await bot.PrivateChat("Nick").SendsPhotoAsync("jpeg bytes"u8.ToArray());

        // Act
        var stored = bot.Api.StoreUserPhoto("jpeg bytes"u8.ToArray());

        // Assert
        using (new AssertionScope())
        {
            stored.Select(x => $"{x.Width}x{x.Height}").Should().Equal("90x68", "320x240", "800x600", "1280x960");
            stored
                .Select(x => (x.Width, x.Height, x.FileSize))
                .Should()
                .Equal(sent.Message.Photo!.Select(x => (x.Width, x.Height, x.FileSize)));
        }
    }

    [Fact]
    public async Task StoreUserPhoto_InAHandBuiltUpdate_ShouldBeDownloadableByTheBot()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        var photo = bot.Api.StoreUserPhoto("jpeg bytes"u8.ToArray());

        // Act
        await bot.SendAsync(
            new Update
            {
                Message = new Message
                {
                    Id = 1,
                    Date = DateTime.UtcNow,
                    Chat = new Chat { Id = nick.Chat.Id, Type = ChatType.Private },
                    From = new User { Id = nick.Id, FirstName = "Nick" },
                    Photo = photo,
                },
            }
        );

        // Assert
        nick.LastMessage.Text.Should().Be("Got a photo: jpeg bytes");
    }

    [Fact]
    public async Task StoreUserDocument_ShouldBeDownloadableAfterGetFile()
    {
        // Arrange
        var api = new FakeBotApi();
        var document = api.StoreUserDocument("a,b\n1,2"u8.ToArray(), "report.csv");
        using var content = new MemoryStream();

        // Act
        await api.CreateClient().GetInfoAndDownloadFile(document.FileId, content);

        // Assert
        using (new AssertionScope())
        {
            document.FileName.Should().Be("report.csv");
            document.MimeType.Should().Be("text/csv");
            Encoding.UTF8.GetString(content.ToArray()).Should().Be("a,b\n1,2");
        }
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData(2.5, 3)]
    public void StoreUserVoice_ShouldCarryItsDuration(double? seconds, int duration)
    {
        // Arrange
        var api = new FakeBotApi();

        // Act
        var voice = api.StoreUserVoice(
            "ogg bytes"u8.ToArray(),
            seconds is { } value ? TimeSpan.FromSeconds(value) : null
        );

        // Assert
        using (new AssertionScope())
        {
            voice.Duration.Should().Be(duration);
            voice.MimeType.Should().Be("audio/ogg");
        }
    }

    [Theory]
    [InlineData("photo")]
    [InlineData("document")]
    [InlineData("voice")]
    public void StoreUserFiles_WithEmptyContent_ShouldThrow(string kind)
    {
        // Arrange
        var api = new FakeBotApi();
        Action act = kind switch
        {
            "photo" => () => api.StoreUserPhoto([]),
            "document" => () => api.StoreUserDocument([], "report.csv"),
            _ => () => api.StoreUserVoice([]),
        };

        // Act
        var thrown = act.Should().Throw<ArgumentException>().Which;

        // Assert
        thrown.Message.Should().StartWith("The Telegram app never sends an empty file.");
    }
}
