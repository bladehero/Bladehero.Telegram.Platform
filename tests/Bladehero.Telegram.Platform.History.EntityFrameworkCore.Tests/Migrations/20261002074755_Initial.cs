using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bladehero.Telegram.Platform.History.EntityFrameworkCore.Tests.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "TelegramHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                    Time = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UpdateId = table.Column<int>(type: "INTEGER", nullable: true),
                    ChatId = table.Column<long>(type: "INTEGER", nullable: true),
                    UserId = table.Column<long>(type: "INTEGER", nullable: true),
                    MessageId = table.Column<int>(type: "INTEGER", nullable: true),
                    InlineMessageId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Text = table.Column<string>(type: "TEXT", nullable: true),
                    FileId = table.Column<string>(type: "TEXT", nullable: true),
                    FileName = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorCode = table.Column<int>(type: "INTEGER", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    Json = table.Column<string>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramHistory", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TelegramHistory_ChatId_Id",
                table: "TelegramHistory",
                columns: new[] { "ChatId", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TelegramHistory_ChatId_MessageId",
                table: "TelegramHistory",
                columns: new[] { "ChatId", "MessageId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TelegramHistory_InlineMessageId",
                table: "TelegramHistory",
                column: "InlineMessageId"
            );

            migrationBuilder.CreateIndex(name: "IX_TelegramHistory_Time", table: "TelegramHistory", column: "Time");

            migrationBuilder.CreateIndex(
                name: "IX_TelegramHistory_UpdateId",
                table: "TelegramHistory",
                column: "UpdateId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Expenses");

            migrationBuilder.DropTable(name: "TelegramHistory");
        }
    }
}
