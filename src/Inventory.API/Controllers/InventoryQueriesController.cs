using Inventory.Application.Features.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Authorization;
using Shared.Authorization.AspNetCore;
using Shared.Contracts.Common;
using Shared.Contracts.Inventory;

namespace Inventory.API.Controllers;

[Authorize]
[RequirePermission(Permissions.Inventory.Read)]
[Route("api/v2/inventory")]
[ApiExplorerSettings(GroupName = "v2")]
public sealed class InventoryQueriesController : ApiControllerBase
{
    [HttpGet("stock")]
    public async Task<ActionResult<PagedResult<StockDto>>> Stock([FromQuery] GetStockPageQuery query, CancellationToken ct)
        => Ok(await Sender.Send(query, ct));

    [HttpGet("kardex")]
    public async Task<ActionResult<PagedResult<KardexEntryDto>>> Kardex([FromQuery] GetKardexPageQuery query, CancellationToken ct)
        => Ok(await Sender.Send(query, ct));
}
