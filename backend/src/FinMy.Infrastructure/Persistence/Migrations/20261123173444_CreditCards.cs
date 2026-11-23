using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreditCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "credit_cards",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name_normalized = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    closing_day = table.Column<short>(type: "smallint", nullable: false),
                    due_day = table.Column<short>(type: "smallint", nullable: false),
                    payment_wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    limit_cents = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("pk_credit_cards", x => x.id);
                    table.UniqueConstraint("ak_credit_card_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_credit_cards_closing_day", "closing_day BETWEEN 1 AND 31");
                    table.CheckConstraint("ck_credit_cards_color", "color ~ '^#[0-9A-Fa-f]{6}$'");
                    table.CheckConstraint("ck_credit_cards_due_day", "due_day BETWEEN 1 AND 31");
                    table.CheckConstraint("ck_credit_cards_limit_cents", "limit_cents IS NULL OR (limit_cents >= 0 AND limit_cents <= 999999999)");
                    table.ForeignKey(
                        name: "fk_credit_cards_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_credit_cards_wallet_tenant_id_payment_wallet_id",
                        columns: x => new { x.tenant_id, x.payment_wallet_id },
                        principalTable: "wallets",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "installment_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    credit_card_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    mode = table.Column<string>(type: "text", nullable: false),
                    installment_total = table.Column<short>(type: "smallint", nullable: false),
                    first_number = table.Column<short>(type: "smallint", nullable: false),
                    total_amount_cents = table.Column<long>(type: "bigint", nullable: true),
                    installment_amount_cents = table.Column<long>(type: "bigint", nullable: true),
                    purchase_date = table.Column<DateOnly>(type: "date", nullable: false),
                    first_invoice_due_month = table.Column<DateOnly>(type: "date", nullable: false),
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
                    table.PrimaryKey("pk_installment_plans", x => x.id);
                    table.UniqueConstraint("ak_installment_plan_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_installment_plans_amount_by_mode", "(mode = 'total_amount' AND total_amount_cents IS NOT NULL AND installment_amount_cents IS NULL) OR (mode <> 'total_amount' AND installment_amount_cents IS NOT NULL AND total_amount_cents IS NULL)");
                    table.CheckConstraint("ck_installment_plans_first_invoice_due_month", "extract(day from first_invoice_due_month) = 1 AND first_invoice_due_month BETWEEN DATE '2000-01-01' AND DATE '2100-12-31'");
                    table.CheckConstraint("ck_installment_plans_first_number", "first_number BETWEEN 1 AND installment_total");
                    table.CheckConstraint("ck_installment_plans_first_number_mode", "mode = 'in_progress' OR first_number = 1");
                    table.CheckConstraint("ck_installment_plans_installment_amount_cents", "installment_amount_cents IS NULL OR (installment_amount_cents > 0 AND installment_amount_cents <= 999999999)");
                    table.CheckConstraint("ck_installment_plans_installment_total", "installment_total BETWEEN 1 AND 48");
                    table.CheckConstraint("ck_installment_plans_mode", "mode IN ('total_amount', 'installment_amount', 'in_progress')");
                    table.CheckConstraint("ck_installment_plans_purchase_date", "purchase_date BETWEEN DATE '2000-01-01' AND DATE '2100-12-31'");
                    table.CheckConstraint("ck_installment_plans_total_amount_cents", "total_amount_cents IS NULL OR (total_amount_cents > 0 AND total_amount_cents <= 999999999)");
                    table.ForeignKey(
                        name: "fk_installment_plans_credit_cards_tenant_id_credit_card_id",
                        columns: x => new { x.tenant_id, x.credit_card_id },
                        principalTable: "credit_cards",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_installment_plans_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    credit_card_id = table.Column<Guid>(type: "uuid", nullable: false),
                    due_month = table.Column<DateOnly>(type: "date", nullable: false),
                    closing_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
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
                    table.PrimaryKey("pk_invoices", x => x.id);
                    table.UniqueConstraint("ak_invoices_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.UniqueConstraint("ak_invoices_tenant_id_id_credit_card_id", x => new { x.tenant_id, x.id, x.credit_card_id });
                    table.CheckConstraint("ck_invoices_closing_date", "closing_date BETWEEN DATE '2000-01-01' AND DATE '2100-12-31'");
                    table.CheckConstraint("ck_invoices_due_date", "due_date BETWEEN DATE '2000-01-01' AND DATE '2100-12-31'");
                    table.CheckConstraint("ck_invoices_due_month", "extract(day from due_month) = 1 AND due_month BETWEEN DATE '2000-01-01' AND DATE '2100-12-31'");
                    table.ForeignKey(
                        name: "fk_invoices_credit_cards_tenant_id_credit_card_id",
                        columns: x => new { x.tenant_id, x.credit_card_id },
                        principalTable: "credit_cards",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "card_purchases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    credit_card_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description_normalized = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    nature = table.Column<string>(type: "text", nullable: false),
                    purchase_date = table.Column<DateOnly>(type: "date", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: false),
                    reconciled = table.Column<bool>(type: "boolean", nullable: false),
                    manually_moved = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    installment_plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    installment_number = table.Column<short>(type: "smallint", nullable: true),
                    installment_total = table.Column<short>(type: "smallint", nullable: true),
                    carry_over_pair_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_card_purchases", x => x.id);
                    table.UniqueConstraint("ak_card_purchases_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_card_purchases_amount_cents", "amount_cents > 0 AND amount_cents <= 999999999");
                    table.CheckConstraint("ck_card_purchases_carry_over_pair", "(origin = 'carry_over') = (carry_over_pair_id IS NOT NULL)");
                    table.CheckConstraint("ck_card_purchases_credit_not_installment", "nature = 'debit' OR installment_plan_id IS NULL");
                    table.CheckConstraint("ck_card_purchases_installment_fields", "(installment_plan_id IS NULL) = (installment_number IS NULL) AND (installment_plan_id IS NULL) = (installment_total IS NULL) AND ((origin = 'installment') = (installment_plan_id IS NOT NULL))");
                    table.CheckConstraint("ck_card_purchases_installment_number", "installment_number IS NULL OR (installment_number BETWEEN 1 AND installment_total)");
                    table.CheckConstraint("ck_card_purchases_installment_total", "installment_total IS NULL OR installment_total BETWEEN 1 AND 48");
                    table.CheckConstraint("ck_card_purchases_nature", "nature IN ('debit', 'credit')");
                    table.CheckConstraint("ck_card_purchases_origin", "origin IN ('manual', 'installment', 'carry_over')");
                    table.CheckConstraint("ck_card_purchases_purchase_date", "purchase_date BETWEEN DATE '2000-01-01' AND DATE '2100-12-31'");
                    table.ForeignKey(
                        name: "fk_card_purchases_credit_card_tenant_id_credit_card_id",
                        columns: x => new { x.tenant_id, x.credit_card_id },
                        principalTable: "credit_cards",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_card_purchases_installment_plan_tenant_id_installment_plan_",
                        columns: x => new { x.tenant_id, x.installment_plan_id },
                        principalTable: "installment_plans",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_card_purchases_invoice_tenant_id_invoice_id_credit_card_id",
                        columns: x => new { x.tenant_id, x.invoice_id, x.credit_card_id },
                        principalTable: "invoices",
                        principalColumns: new[] { "tenant_id", "id", "credit_card_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_card_purchases_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoice_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    payment_date = table.Column<DateOnly>(type: "date", nullable: false),
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
                    table.PrimaryKey("pk_invoice_payments", x => x.id);
                    table.UniqueConstraint("ak_invoice_payments_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_invoice_payments_amount_cents", "amount_cents > 0");
                    table.CheckConstraint("ck_invoice_payments_payment_date", "payment_date BETWEEN DATE '2000-01-01' AND DATE '2100-12-31'");
                    table.ForeignKey(
                        name: "fk_invoice_payments_invoices_tenant_id_invoice_id",
                        columns: x => new { x.tenant_id, x.invoice_id },
                        principalTable: "invoices",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoice_payments_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoice_payments_wallet_tenant_id_wallet_id",
                        columns: x => new { x.tenant_id, x.wallet_id },
                        principalTable: "wallets",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_card_purchases_tenant_id_carry_over_pair_id",
                table: "card_purchases",
                columns: new[] { "tenant_id", "carry_over_pair_id" },
                filter: "carry_over_pair_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_card_purchases_tenant_id_credit_card_id",
                table: "card_purchases",
                columns: new[] { "tenant_id", "credit_card_id" });

            migrationBuilder.CreateIndex(
                name: "ix_card_purchases_tenant_id_description_normalized",
                table: "card_purchases",
                columns: new[] { "tenant_id", "description_normalized" });

            migrationBuilder.CreateIndex(
                name: "ix_card_purchases_tenant_id_installment_plan_id",
                table: "card_purchases",
                columns: new[] { "tenant_id", "installment_plan_id" });

            migrationBuilder.CreateIndex(
                name: "ix_card_purchases_tenant_id_invoice_id",
                table: "card_purchases",
                columns: new[] { "tenant_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_card_purchases_tenant_id_invoice_id_credit_card_id",
                table: "card_purchases",
                columns: new[] { "tenant_id", "invoice_id", "credit_card_id" });

            migrationBuilder.CreateIndex(
                name: "ix_credit_cards_tenant_id_name_normalized",
                table: "credit_cards",
                columns: new[] { "tenant_id", "name_normalized" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_credit_cards_tenant_id_payment_wallet_id",
                table: "credit_cards",
                columns: new[] { "tenant_id", "payment_wallet_id" });

            migrationBuilder.CreateIndex(
                name: "ix_installment_plans_tenant_id_credit_card_id",
                table: "installment_plans",
                columns: new[] { "tenant_id", "credit_card_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_payments_tenant_id_invoice_id",
                table: "invoice_payments",
                columns: new[] { "tenant_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_payments_tenant_id_wallet_id",
                table: "invoice_payments",
                columns: new[] { "tenant_id", "wallet_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_tenant_id_credit_card_id_due_month",
                table: "invoices",
                columns: new[] { "tenant_id", "credit_card_id", "due_month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_tenant_id_due_month",
                table: "invoices",
                columns: new[] { "tenant_id", "due_month" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "card_purchases");

            migrationBuilder.DropTable(
                name: "invoice_payments");

            migrationBuilder.DropTable(
                name: "installment_plans");

            migrationBuilder.DropTable(
                name: "invoices");

            migrationBuilder.DropTable(
                name: "credit_cards");
        }
    }
}
