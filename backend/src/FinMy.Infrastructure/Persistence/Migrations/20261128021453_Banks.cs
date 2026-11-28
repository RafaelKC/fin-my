using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Banks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bank",
                table: "wallets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank",
                table: "credit_cards",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bank",
                table: "wallets");

            migrationBuilder.DropColumn(
                name: "bank",
                table: "credit_cards");
        }
    }
}
