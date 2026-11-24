using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OAuthUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "oauth_authorization_usage",
                columns: table => new
                {
                    authorization_id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oauth_authorization_usage", x => x.authorization_id);
                    table.ForeignKey(
                        name: "fk_oauth_authorization_usage_user_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_oauth_authorization_usage_user_id",
                table: "oauth_authorization_usage",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "oauth_authorization_usage");
        }
    }
}
