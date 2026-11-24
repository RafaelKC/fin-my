using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceEstimates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "estimate_cents",
                table: "invoices",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_estimate_cents",
                table: "invoices",
                sql: "estimate_cents IS NULL OR (estimate_cents > 0 AND estimate_cents <= 999999999)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_estimate_cents",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "estimate_cents",
                table: "invoices");
        }
    }
}
