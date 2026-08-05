using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MoneroUserForum.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddE2eeKeysAndEnvelope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EncryptedEnvelope",
                table: "PrivateMessages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "E2eeEnabledUtc",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncPublicKey",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KdfSalt",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoverySalt",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryWrappedPrivateKeys",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignPublicKey",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WrappedPrivateKeys",
                table: "AspNetUsers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EncryptedEnvelope",
                table: "PrivateMessages");

            migrationBuilder.DropColumn(
                name: "E2eeEnabledUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "EncPublicKey",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "KdfSalt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "RecoverySalt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "RecoveryWrappedPrivateKeys",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SignPublicKey",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "WrappedPrivateKeys",
                table: "AspNetUsers");
        }
    }
}
