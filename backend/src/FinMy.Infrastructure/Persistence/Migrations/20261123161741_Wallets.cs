using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Wallets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wallets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name_normalized = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    initial_balance_cents = table.Column<long>(type: "bigint", nullable: false),
                    initial_balance_date = table.Column<DateOnly>(type: "date", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_wallets", x => x.id);
                    table.UniqueConstraint("ak_wallet_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_wallets_color", "color ~ '^#[0-9A-Fa-f]{6}$'");
                    table.CheckConstraint("ck_wallets_initial_balance_cents", "abs(initial_balance_cents) <= 999999999");
                    table.CheckConstraint("ck_wallets_initial_balance_date", "initial_balance_date BETWEEN DATE '2000-01-01' AND DATE '2100-12-31'");
                    table.CheckConstraint("ck_wallets_primary_not_archived", "NOT (is_primary AND archived_at IS NOT NULL)");
                    table.CheckConstraint("ck_wallets_type", "type IN ('checking', 'digital', 'savings', 'cash', 'meal_voucher', 'other')");
                    table.ForeignKey(
                        name: "fk_wallets_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description_normalized = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reference_month = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("pk_entries", x => x.id);
                    table.UniqueConstraint("ak_entries_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_entries_amount_cents", "amount_cents > 0 AND amount_cents <= 999999999");
                    table.CheckConstraint("ck_entries_entry_date", "entry_date BETWEEN DATE '2000-01-01' AND DATE '2100-12-31'");
                    table.CheckConstraint("ck_entries_kind", "kind IN ('income', 'expense')");
                    table.CheckConstraint("ck_entries_reference_month", "extract(day from reference_month) = 1 AND reference_month BETWEEN DATE '2000-01-01' AND DATE '2100-12-31'");
                    table.CheckConstraint("ck_entries_status", "status IN ('planned', 'settled')");
                    table.ForeignKey(
                        name: "fk_entries_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_entries_wallet_tenant_id_wallet_id",
                        columns: x => new { x.tenant_id, x.wallet_id },
                        principalTable: "wallets",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transfers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    transfer_date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description_normalized = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("pk_transfers", x => x.id);
                    table.UniqueConstraint("ak_transfers_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_transfers_amount_cents", "amount_cents > 0 AND amount_cents <= 999999999");
                    table.CheckConstraint("ck_transfers_different_wallets", "from_wallet_id <> to_wallet_id");
                    table.CheckConstraint("ck_transfers_transfer_date", "transfer_date BETWEEN DATE '2000-01-01' AND DATE '2100-12-31'");
                    table.ForeignKey(
                        name: "fk_transfers_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transfers_wallet_tenant_id_from_wallet_id",
                        columns: x => new { x.tenant_id, x.from_wallet_id },
                        principalTable: "wallets",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transfers_wallet_tenant_id_to_wallet_id",
                        columns: x => new { x.tenant_id, x.to_wallet_id },
                        principalTable: "wallets",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entries_tenant_id_description_normalized",
                table: "entries",
                columns: new[] { "tenant_id", "description_normalized" });

            migrationBuilder.CreateIndex(
                name: "ix_entries_tenant_id_reference_month",
                table: "entries",
                columns: new[] { "tenant_id", "reference_month" });

            migrationBuilder.CreateIndex(
                name: "ix_entries_tenant_id_status_reference_month",
                table: "entries",
                columns: new[] { "tenant_id", "status", "reference_month" });

            migrationBuilder.CreateIndex(
                name: "ix_entries_tenant_id_wallet_id",
                table: "entries",
                columns: new[] { "tenant_id", "wallet_id" });

            migrationBuilder.CreateIndex(
                name: "ix_transfers_tenant_id_from_wallet_id",
                table: "transfers",
                columns: new[] { "tenant_id", "from_wallet_id" });

            migrationBuilder.CreateIndex(
                name: "ix_transfers_tenant_id_to_wallet_id",
                table: "transfers",
                columns: new[] { "tenant_id", "to_wallet_id" });

            migrationBuilder.CreateIndex(
                name: "ix_transfers_tenant_id_transfer_date",
                table: "transfers",
                columns: new[] { "tenant_id", "transfer_date" });

            migrationBuilder.CreateIndex(
                name: "ix_wallets_tenant_id_name_normalized",
                table: "wallets",
                columns: new[] { "tenant_id", "name_normalized" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_wallets_tenant_id_primary",
                table: "wallets",
                column: "tenant_id",
                unique: true,
                filter: "is_primary AND archived_at IS NULL AND deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entries");

            migrationBuilder.DropTable(
                name: "transfers");

            migrationBuilder.DropTable(
                name: "wallets");
        }
    }
}
