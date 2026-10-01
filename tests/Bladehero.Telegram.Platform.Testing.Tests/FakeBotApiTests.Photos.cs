using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class FakeBotApiTests
{
    [Fact]
    public async Task SendPhoto_ShouldGiveFourSizesSmallestFirst()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

        // Act
        var sent = await client.SendPhoto(Chat, Jpeg("cat"));

        // Assert
        using (new AssertionScope())
        {
            sent.Photo!.Select(x => $"{x.Width}x{x.Height}").Should().Equal("90x68", "320x240", "800x600", "1280x960");
            sent.Photo!.Select(x => x.FileId).Should().OnlyHaveUniqueItems();
            sent.Photo!.Select(x => x.FileUniqueId).Should().OnlyHaveUniqueItems();
        }
    }

    [Fact]
    public async Task SendPhoto_OnlyTheLargestSize_ShouldDownloadThePhoto()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var sent = await client.SendPhoto(Chat, Jpeg("cat"));

        // Act
        var downloads = new List<string>();
        foreach (var size in sent.Photo!)
        {
            using var content = new MemoryStream();
            await client.GetInfoAndDownloadFile(size.FileId, content);
            downloads.Add(System.Text.Encoding.UTF8.GetString(content.ToArray()));
        }

        // Assert
        downloads.Should().Equal("thumbnail", "thumbnail", "thumbnail", "cat");
    }

    [Fact]
    public async Task SendPhoto_ByTheFileIdOfAnySize_ShouldGiveTheSameSizes()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var sent = await client.SendPhoto(Chat, Jpeg("cat"));

        // Act
        var again = await client.SendPhoto(Chat, InputFile.FromFileId(sent.Photo![0].FileId));

        // Assert
        again.Photo!.Select(x => x.FileId).Should().Equal(sent.Photo!.Select(x => x.FileId));
    }
}
