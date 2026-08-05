using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MoneroUserForum.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileMonericaUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MonericaUrl",
                table: "AspNetUsers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MonericaUrl",
                table: "AspNetUsers");
        }
    }
}
