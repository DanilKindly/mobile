using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NETmessenger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReliableDeliveryAndCursor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientMessageId",
                table: "Messages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SentAtClient",
                table: "Messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "Messages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ChatId_SenderId_ClientMessageId",
                table: "Messages",
                columns: new[] { "ChatId", "SenderId", "ClientMessageId" },
                unique: true,
                filter: "\"ClientMessageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_Version",
                table: "Messages",
                column: "Version");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Messages_ChatId_SenderId_ClientMessageId",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_Version",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ClientMessageId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "SentAtClient",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Messages");
        }
    }
}
