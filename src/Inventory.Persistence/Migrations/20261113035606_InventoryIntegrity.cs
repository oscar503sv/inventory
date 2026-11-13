using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Inventory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InventoryIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "stocks",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "IdempotencyKey",
                table: "inventory_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LedgerSequence",
                table: "inventory_movements",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "inventory_movements",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // Preserve the old ledger ordering when upgrading populated databases.
            migrationBuilder.Sql("""
                WITH ordered AS (
                    SELECT "Id", row_number() OVER (ORDER BY "CreatedAtUtc", "MovementNumber", "Id") AS position
                    FROM inventory_movements
                )
                UPDATE inventory_movements AS m SET "LedgerSequence" = ordered.position
                FROM ordered WHERE ordered."Id" = m."Id";
                SELECT setval(pg_get_serial_sequence('inventory_movements', 'LedgerSequence'),
                    COALESCE(MAX("LedgerSequence"), 1), COUNT(*) > 0) FROM inventory_movements;
                """);

            // One-time security cleanup: a downgrade intentionally cannot restore leaked credentials.
            migrationBuilder.Sql("""
                UPDATE audit_logs SET
                    "OldValues" = CASE WHEN "OldValues" IS NULL THEN NULL ELSE
                        COALESCE((SELECT jsonb_object_agg(key, value) FROM jsonb_each("OldValues")
                            WHERE key !~* '(password|hash|token|secret)'), '{}'::jsonb) END,
                    "NewValues" = CASE WHEN "NewValues" IS NULL THEN NULL ELSE
                        COALESCE((SELECT jsonb_object_agg(key, value) FROM jsonb_each("NewValues")
                            WHERE key !~* '(password|hash|token|secret)'), '{}'::jsonb) END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_CreatedBy_IdempotencyKey",
                table: "inventory_movements",
                columns: new[] { "CreatedBy", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_LedgerSequence",
                table: "inventory_movements",
                column: "LedgerSequence",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_inventory_movements_CreatedBy_IdempotencyKey",
                table: "inventory_movements");

            migrationBuilder.DropIndex(
                name: "IX_inventory_movements_LedgerSequence",
                table: "inventory_movements");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "stocks");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "inventory_movements");

            migrationBuilder.DropColumn(
                name: "LedgerSequence",
                table: "inventory_movements");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "inventory_movements");
        }
    }
}
