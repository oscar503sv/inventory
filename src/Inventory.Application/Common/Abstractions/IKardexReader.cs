using Shared.Contracts.Common;
using Shared.Contracts.Inventory;

namespace Inventory.Application.Common.Abstractions;

public interface IKardexReader
{
    Task<PagedResult<KardexEntryDto>> ReadAsync(Guid product, Guid warehouse, DateTime? fromUtc, DateTime? toUtc,
        int page, int pageSize, CancellationToken cancellationToken);
}
