using System.Globalization;
using Inventory.Application.Common.Abstractions;
using Inventory.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Persistence.Inventory;

/// <summary>Allocates unique numbers across requests and API instances using a non-cycling PostgreSQL sequence.</summary>
public sealed class SequenceMovementNumberGenerator(AppDbContext db) : IMovementNumberGenerator
{
    public async Task<string> NextAsync(MovementType type, CancellationToken cancellationToken = default)
    {
        var prefix = type switch
        {
            MovementType.Entry => "ENT",
            MovementType.Exit => "EXI",
            MovementType.Transfer => "TRF",
            MovementType.Adjustment => "ADJ",
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        var sequence = await db.Database.SqlQuery<long>($"SELECT nextval('inventory_movement_number_seq') AS \"Value\"")
            .SingleAsync(cancellationToken);
        // The 19-digit suffix fits every positive bigint and is disjoint from historical date-based numbers.
        return prefix + "-" + sequence.ToString("D19", CultureInfo.InvariantCulture);
    }
}
