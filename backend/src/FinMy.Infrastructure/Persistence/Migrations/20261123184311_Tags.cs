using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Tags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "settlement_tag_id",
                table: "entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "split_group_id",
                table: "entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "settlement_tag_id",
                table: "card_purchases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "split_group_id",
                table: "card_purchases",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name_normalized = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    is_settlement = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_via = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_via = table.Column<string>(type: "text", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deletion_batch_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tags", x => x.id);
                    table.UniqueConstraint("ak_tag_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_tags_color", "color ~ '^#[0-9A-Fa-f]{6}$'");
                    table.ForeignKey(
                        name: "fk_tags_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "card_purchase_tags",
                columns: table => new
                {
                    card_purchase_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_purchase_tags", x => new { x.card_purchase_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_card_purchase_tags_card_purchases_tenant_id_card_purchase_id",
                        columns: x => new { x.tenant_id, x.card_purchase_id },
                        principalTable: "card_purchases",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_card_purchase_tags_tag_tenant_id_tag_id",
                        columns: x => new { x.tenant_id, x.tag_id },
                        principalTable: "tags",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_card_purchase_tags_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "entry_tags",
                columns: table => new
                {
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entry_tags", x => new { x.entry_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_entry_tags_entries_tenant_id_entry_id",
                        columns: x => new { x.tenant_id, x.entry_id },
                        principalTable: "entries",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_entry_tags_tag_tenant_id_tag_id",
                        columns: x => new { x.tenant_id, x.tag_id },
                        principalTable: "tags",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_entry_tags_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entries_tenant_id_settlement_tag_id",
                table: "entries",
                columns: new[] { "tenant_id", "settlement_tag_id" });

            migrationBuilder.CreateIndex(
                name: "ix_entries_tenant_id_split_group_id",
                table: "entries",
                columns: new[] { "tenant_id", "split_group_id" },
                filter: "split_group_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_card_purchases_tenant_id_settlement_tag_id",
                table: "card_purchases",
                columns: new[] { "tenant_id", "settlement_tag_id" });

            migrationBuilder.CreateIndex(
                name: "ix_card_purchases_tenant_id_split_group_id",
                table: "card_purchases",
                columns: new[] { "tenant_id", "split_group_id" },
                filter: "split_group_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_card_purchase_tags_tenant_id_card_purchase_id",
                table: "card_purchase_tags",
                columns: new[] { "tenant_id", "card_purchase_id" });

            migrationBuilder.CreateIndex(
                name: "ix_card_purchase_tags_tenant_id_tag_id",
                table: "card_purchase_tags",
                columns: new[] { "tenant_id", "tag_id" });

            migrationBuilder.CreateIndex(
                name: "ix_entry_tags_tenant_id_entry_id",
                table: "entry_tags",
                columns: new[] { "tenant_id", "entry_id" });

            migrationBuilder.CreateIndex(
                name: "ix_entry_tags_tenant_id_tag_id",
                table: "entry_tags",
                columns: new[] { "tenant_id", "tag_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tags_tenant_id_name_normalized",
                table: "tags",
                columns: new[] { "tenant_id", "name_normalized" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_card_purchases_tag_tenant_id_settlement_tag_id",
                table: "card_purchases",
                columns: new[] { "tenant_id", "settlement_tag_id" },
                principalTable: "tags",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_entries_tag_tenant_id_settlement_tag_id",
                table: "entries",
                columns: new[] { "tenant_id", "settlement_tag_id" },
                principalTable: "tags",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_card_purchases_tag_tenant_id_settlement_tag_id",
                table: "card_purchases");

            migrationBuilder.DropForeignKey(
                name: "fk_entries_tag_tenant_id_settlement_tag_id",
                table: "entries");

            migrationBuilder.DropTable(
                name: "card_purchase_tags");

            migrationBuilder.DropTable(
                name: "entry_tags");

            migrationBuilder.DropTable(
                name: "tags");

            migrationBuilder.DropIndex(
                name: "ix_entries_tenant_id_settlement_tag_id",
                table: "entries");

            migrationBuilder.DropIndex(
                name: "ix_entries_tenant_id_split_group_id",
                table: "entries");

            migrationBuilder.DropIndex(
                name: "ix_card_purchases_tenant_id_settlement_tag_id",
                table: "card_purchases");

            migrationBuilder.DropIndex(
                name: "ix_card_purchases_tenant_id_split_group_id",
                table: "card_purchases");

            migrationBuilder.DropColumn(
                name: "settlement_tag_id",
                table: "entries");

            migrationBuilder.DropColumn(
                name: "split_group_id",
                table: "entries");

            migrationBuilder.DropColumn(
                name: "settlement_tag_id",
                table: "card_purchases");

            migrationBuilder.DropColumn(
                name: "split_group_id",
                table: "card_purchases");
        }
    }
}
