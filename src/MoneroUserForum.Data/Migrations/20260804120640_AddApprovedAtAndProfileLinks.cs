using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MoneroUserForum.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovedAtAndProfileLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAtUtc",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Link1Label",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Link1Url",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Link2Label",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Link2Url",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Link3Label",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Link3Url",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            // Backfill: accounts already approved before this field existed get their
            // approval clock set to when they joined, so long-standing members can post
            // images immediately instead of waiting a fresh month.
            migrationBuilder.Sql(
                "UPDATE \"AspNetUsers\" SET \"ApprovedAtUtc\" = \"CreatedAtUtc\" " +
                "WHERE \"IsApproved\" = TRUE AND \"ApprovedAtUtc\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Link1Label",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Link1Url",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Link2Label",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Link2Url",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Link3Label",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Link3Url",
                table: "AspNetUsers");
        }
    }
}
