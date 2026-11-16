using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InventoryRelationalConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Validate legacy rows before changing constraints. Never repair or discard history here.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM stocks s
                        LEFT JOIN products p ON p."Id" = s."ProductId"
                        LEFT JOIN warehouses w ON w."Id" = s."WarehouseId"
                        WHERE p."Id" IS NULL OR w."Id" IS NULL
                            OR s."Quantity" < 0 OR s."ReservedQuantity" < 0
                            OR s."ReservedQuantity" > s."Quantity"
                    ) THEN
                        RAISE EXCEPTION 'Inventory integrity precheck failed: invalid stocks. Review existing data before retrying.';
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM inventory_movements m
                        LEFT JOIN warehouses w ON w."Id" = m."WarehouseId"
                        LEFT JOIN warehouses d ON d."Id" = m."DestinationWarehouseId"
                        WHERE w."Id" IS NULL OR (m."DestinationWarehouseId" IS NOT NULL AND d."Id" IS NULL)
                    ) THEN
                        RAISE EXCEPTION 'Inventory integrity precheck failed: orphaned movement warehouses. Review existing data before retrying.';
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM inventory_movement_lines l
                        LEFT JOIN products p ON p."Id" = l."ProductId"
                        WHERE p."Id" IS NULL OR l."Quantity" <= 0 OR l."UnitCost" < 0
                    ) THEN
                        RAISE EXCEPTION 'Inventory integrity precheck failed: invalid movement lines. Review existing data before retrying.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_inventory_movement_lines_inventory_movements_MovementId",
                table: "inventory_movement_lines");

            migrationBuilder.CreateIndex(
                name: "IX_stocks_WarehouseId",
                table: "stocks",
                column: "WarehouseId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_stocks_Quantity",
                table: "stocks",
                sql: "\"Quantity\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_stocks_ReservedQuantity",
                table: "stocks",
                sql: "\"ReservedQuantity\" >= 0 AND \"ReservedQuantity\" <= \"Quantity\"");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_DestinationWarehouseId",
                table: "inventory_movements",
                column: "DestinationWarehouseId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_inventory_movement_lines_Quantity",
                table: "inventory_movement_lines",
                sql: "\"Quantity\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_inventory_movement_lines_UnitCost",
                table: "inventory_movement_lines",
                sql: "\"UnitCost\" >= 0");

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_movement_lines_inventory_movements_MovementId",
                table: "inventory_movement_lines",
                column: "MovementId",
                principalTable: "inventory_movements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_movement_lines_products_ProductId",
                table: "inventory_movement_lines",
                column: "ProductId",
                principalTable: "products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_movements_warehouses_DestinationWarehouseId",
                table: "inventory_movements",
                column: "DestinationWarehouseId",
                principalTable: "warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_movements_warehouses_WarehouseId",
                table: "inventory_movements",
                column: "WarehouseId",
                principalTable: "warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_stocks_products_ProductId",
                table: "stocks",
                column: "ProductId",
                principalTable: "products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_stocks_warehouses_WarehouseId",
                table: "stocks",
                column: "WarehouseId",
                principalTable: "warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_inventory_movement_lines_inventory_movements_MovementId",
                table: "inventory_movement_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_inventory_movement_lines_products_ProductId",
                table: "inventory_movement_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_inventory_movements_warehouses_DestinationWarehouseId",
                table: "inventory_movements");

            migrationBuilder.DropForeignKey(
                name: "FK_inventory_movements_warehouses_WarehouseId",
                table: "inventory_movements");

            migrationBuilder.DropForeignKey(
                name: "FK_stocks_products_ProductId",
                table: "stocks");

            migrationBuilder.DropForeignKey(
                name: "FK_stocks_warehouses_WarehouseId",
                table: "stocks");

            migrationBuilder.DropIndex(
                name: "IX_stocks_WarehouseId",
                table: "stocks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_stocks_Quantity",
                table: "stocks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_stocks_ReservedQuantity",
                table: "stocks");

            migrationBuilder.DropIndex(
                name: "IX_inventory_movements_DestinationWarehouseId",
                table: "inventory_movements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_movement_lines_Quantity",
                table: "inventory_movement_lines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_movement_lines_UnitCost",
                table: "inventory_movement_lines");

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_movement_lines_inventory_movements_MovementId",
                table: "inventory_movement_lines",
                column: "MovementId",
                principalTable: "inventory_movements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
