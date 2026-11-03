using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MoneroUserForum.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPgpVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PgpChallengeArmored",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PgpChallengeExpiresUtc",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PgpChallengeHash",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PgpFingerprint",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PgpVerifiedAtUtc",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PgpChallengeArmored",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PgpChallengeExpiresUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PgpChallengeHash",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PgpFingerprint",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PgpVerifiedAtUtc",
                table: "AspNetUsers");
        }
    }
}
