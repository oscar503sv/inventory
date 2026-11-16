using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MovementNumberSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "inventory_movement_number_seq");

            // A rollback retains movement rows. Re-applying must continue above any previously issued number.
            migrationBuilder.Sql("""
                SELECT setval('inventory_movement_number_seq',
                    GREATEST(COALESCE(MAX(LEAST(substring("MovementNumber" from 5)::numeric,
                        9223372036854775807)::bigint), 0), 1), COUNT(*) > 0)
                FROM inventory_movements
                WHERE "MovementNumber" ~ '^(ENT|EXI|TRF|ADJ)-[0-9]{19}$';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSequence(
                name: "inventory_movement_number_seq");
        }
    }
}
