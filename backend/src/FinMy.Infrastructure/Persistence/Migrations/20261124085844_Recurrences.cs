using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Recurrences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_card_purchases_origin",
                table: "card_purchases");

            migrationBuilder.AddColumn<Guid>(
                name: "recurrence_id",
                table: "entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "recurrence_modified",
                table: "entries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "recurrence_month",
                table: "entries",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "recurrence_id",
                table: "card_purchases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "recurrence_modified",
                table: "card_purchases",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "recurrence_month",
                table: "card_purchases",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "recurrences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description_normalized = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    day_of_month = table.Column<short>(type: "smallint", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    credit_card_id = table.Column<Guid>(type: "uuid", nullable: true),
                    settlement_tag_id = table.Column<Guid>(type: "uuid", nullable: true),
                    start_month = table.Column<DateOnly>(type: "date", nullable: false),
                    end_type = table.Column<string>(type: "text", nullable: false),
                    end_month = table.Column<DateOnly>(type: "date", nullable: true),
                    occurrence_limit = table.Column<short>(type: "smallint", nullable: true),
                    reference_month_rule = table.Column<string>(type: "text", nullable: true),
                    generated_until_month = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("pk_recurrences", x => x.id);
                    table.UniqueConstraint("ak_recurrence_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_recurrences_amount_cents", "amount_cents > 0 AND amount_cents <= 999999999");
                    table.CheckConstraint("ck_recurrences_day_of_month", "day_of_month BETWEEN 1 AND 31");
                    table.CheckConstraint("ck_recurrences_end_month", "end_month IS NULL OR (extract(day from end_month) = 1 AND end_month >= start_month AND end_month <= DATE '2100-12-31')");
                    table.CheckConstraint("ck_recurrences_end_month_by_type", "(end_type = 'none') = (end_month IS NULL)");
                    table.CheckConstraint("ck_recurrences_end_type", "end_type IN ('none', 'until_month', 'after_count')");
                    table.CheckConstraint("ck_recurrences_generated_until_month", "generated_until_month IS NULL OR extract(day from generated_until_month) = 1");
                    table.CheckConstraint("ck_recurrences_kind", "kind IN ('income', 'expense', 'card_purchase')");
                    table.CheckConstraint("ck_recurrences_occurrence_limit", "(end_type = 'after_count' AND occurrence_limit BETWEEN 1 AND 360) OR (end_type <> 'after_count' AND occurrence_limit IS NULL)");
                    table.CheckConstraint("ck_recurrences_reference_month_rule", "reference_month_rule IS NULL OR reference_month_rule IN ('same_month', 'next_month')");
                    table.CheckConstraint("ck_recurrences_start_month", "extract(day from start_month) = 1 AND start_month BETWEEN DATE '2000-01-01' AND DATE '2100-12-31'");
                    table.CheckConstraint("ck_recurrences_target", "(kind = 'card_purchase' AND credit_card_id IS NOT NULL AND wallet_id IS NULL AND reference_month_rule IS NULL) OR (kind <> 'card_purchase' AND wallet_id IS NOT NULL AND credit_card_id IS NULL AND reference_month_rule IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_recurrences_credit_cards_tenant_id_credit_card_id",
                        columns: x => new { x.tenant_id, x.credit_card_id },
                        principalTable: "credit_cards",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recurrences_tag_tenant_id_settlement_tag_id",
                        columns: x => new { x.tenant_id, x.settlement_tag_id },
                        principalTable: "tags",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recurrences_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recurrences_wallet_tenant_id_wallet_id",
                        columns: x => new { x.tenant_id, x.wallet_id },
                        principalTable: "wallets",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recurrence_tags",
                columns: table => new
                {
                    recurrence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recurrence_tags", x => new { x.recurrence_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_recurrence_tags_recurrences_tenant_id_recurrence_id",
                        columns: x => new { x.tenant_id, x.recurrence_id },
                        principalTable: "recurrences",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recurrence_tags_tag_tenant_id_tag_id",
                        columns: x => new { x.tenant_id, x.tag_id },
                        principalTable: "tags",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recurrence_tags_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entries_tenant_id_recurrence_id_recurrence_month",
                table: "entries",
                columns: new[] { "tenant_id", "recurrence_id", "recurrence_month" },
                unique: true,
                filter: "deleted_at IS NULL AND recurrence_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_entries_recurrence_month",
                table: "entries",
                sql: "(recurrence_id IS NULL) = (recurrence_month IS NULL) AND (recurrence_month IS NULL OR extract(day from recurrence_month) = 1)");

            migrationBuilder.CreateIndex(
                name: "ix_card_purchases_tenant_id_recurrence_id_recurrence_month",
                table: "card_purchases",
                columns: new[] { "tenant_id", "recurrence_id", "recurrence_month" },
                unique: true,
                filter: "deleted_at IS NULL AND recurrence_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_card_purchases_origin",
                table: "card_purchases",
                sql: "origin IN ('manual', 'installment', 'recurrence', 'carry_over')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_card_purchases_recurrence",
                table: "card_purchases",
                sql: "(recurrence_id IS NULL) = (recurrence_month IS NULL) AND (recurrence_month IS NULL OR extract(day from recurrence_month) = 1) AND ((origin = 'recurrence') = (recurrence_id IS NOT NULL))");

            migrationBuilder.CreateIndex(
                name: "ix_recurrence_tags_tenant_id_recurrence_id",
                table: "recurrence_tags",
                columns: new[] { "tenant_id", "recurrence_id" });

            migrationBuilder.CreateIndex(
                name: "ix_recurrence_tags_tenant_id_tag_id",
                table: "recurrence_tags",
                columns: new[] { "tenant_id", "tag_id" });

            migrationBuilder.CreateIndex(
                name: "ix_recurrences_tenant_id_credit_card_id",
                table: "recurrences",
                columns: new[] { "tenant_id", "credit_card_id" });

            migrationBuilder.CreateIndex(
                name: "ix_recurrences_tenant_id_settlement_tag_id",
                table: "recurrences",
                columns: new[] { "tenant_id", "settlement_tag_id" });

            migrationBuilder.CreateIndex(
                name: "ix_recurrences_tenant_id_wallet_id",
                table: "recurrences",
                columns: new[] { "tenant_id", "wallet_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_card_purchases_recurrence_tenant_id_recurrence_id",
                table: "card_purchases",
                columns: new[] { "tenant_id", "recurrence_id" },
                principalTable: "recurrences",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_entries_recurrence_tenant_id_recurrence_id",
                table: "entries",
                columns: new[] { "tenant_id", "recurrence_id" },
                principalTable: "recurrences",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_card_purchases_recurrence_tenant_id_recurrence_id",
                table: "card_purchases");

            migrationBuilder.DropForeignKey(
                name: "fk_entries_recurrence_tenant_id_recurrence_id",
                table: "entries");

            migrationBuilder.DropTable(
                name: "recurrence_tags");

            migrationBuilder.DropTable(
                name: "recurrences");

            migrationBuilder.DropIndex(
                name: "ix_entries_tenant_id_recurrence_id_recurrence_month",
                table: "entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_entries_recurrence_month",
                table: "entries");

            migrationBuilder.DropIndex(
                name: "ix_card_purchases_tenant_id_recurrence_id_recurrence_month",
                table: "card_purchases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_card_purchases_origin",
                table: "card_purchases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_card_purchases_recurrence",
                table: "card_purchases");

            migrationBuilder.DropColumn(
                name: "recurrence_id",
                table: "entries");

            migrationBuilder.DropColumn(
                name: "recurrence_modified",
                table: "entries");

            migrationBuilder.DropColumn(
                name: "recurrence_month",
                table: "entries");

            migrationBuilder.DropColumn(
                name: "recurrence_id",
                table: "card_purchases");

            migrationBuilder.DropColumn(
                name: "recurrence_modified",
                table: "card_purchases");

            migrationBuilder.DropColumn(
                name: "recurrence_month",
                table: "card_purchases");

            migrationBuilder.AddCheckConstraint(
                name: "ck_card_purchases_origin",
                table: "card_purchases",
                sql: "origin IN ('manual', 'installment', 'carry_over')");
        }
    }
}
