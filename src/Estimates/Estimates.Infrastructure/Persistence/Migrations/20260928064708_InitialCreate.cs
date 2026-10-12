using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estimates.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "estimates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    customer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    customer_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    customer_phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_estimates", x => x.id);
                    table.CheckConstraint("ck_estimates_approved_at", "(status = 'Approved' AND approved_at IS NOT NULL) OR (status <> 'Approved' AND approved_at IS NULL)");
                    table.CheckConstraint("ck_estimates_currency_code", "char_length(currency_code) = 3");
                    table.CheckConstraint("ck_estimates_status", "status IN ('Draft', 'Approved')");
                    table.CheckConstraint("ck_estimates_version_positive", "version > 0");
                });

            migrationBuilder.CreateTable(
                name: "line_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    estimate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    tax_rate_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_line_items", x => x.id);
                    table.CheckConstraint("ck_line_items_quantity_positive", "quantity > 0");
                    table.CheckConstraint("ck_line_items_tax_rate_range", "tax_rate_percent >= 0 AND tax_rate_percent <= 100");
                    table.CheckConstraint("ck_line_items_total", "total = subtotal + tax_amount");
                    table.CheckConstraint("ck_line_items_unit_price_non_negative", "unit_price >= 0");
                    table.ForeignKey(
                        name: "FK_line_items_estimates_estimate_id",
                        column: x => x.estimate_id,
                        principalTable: "estimates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_estimates_tenant_created_id",
                table: "estimates",
                columns: new[] { "tenant_id", "created_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_line_items_estimate_id",
                table: "line_items",
                column: "estimate_id");

            migrationBuilder.CreateIndex(
                name: "ix_line_items_tenant_estimate",
                table: "line_items",
                columns: new[] { "tenant_id", "estimate_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "line_items");

            migrationBuilder.DropTable(
                name: "estimates");
        }
    }
}
