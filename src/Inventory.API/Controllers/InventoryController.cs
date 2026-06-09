using Inventory.Application.Features.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Authorization;
using Shared.Authorization.AspNetCore;
using Shared.Contracts.Inventory;

namespace Inventory.API.Controllers;

[Authorize]
[Route("api/v1/inventory")]
public sealed class InventoryController : ApiControllerBase
{
    [HttpPost("entries")]
    [RequirePermission(Permissions.Inventory.EntryCreate)]
    public async Task<ActionResult<MovementDto>> CreateEntry([FromBody] CreateEntryCommand command, CancellationToken ct)
        => Ok(await Sender.Send(command, ct));

    [HttpPost("exits")]
    [RequirePermission(Permissions.Inventory.ExitCreate)]
    public async Task<ActionResult<MovementDto>> CreateExit([FromBody] CreateExitCommand command, CancellationToken ct)
        => Ok(await Sender.Send(command, ct));

    [HttpPost("transfers")]
    [RequirePermission(Permissions.Inventory.TransferCreate)]
    public async Task<ActionResult<MovementDto>> CreateTransfer([FromBody] CreateTransferCommand command, CancellationToken ct)
        => Ok(await Sender.Send(command, ct));

    [HttpPost("adjustments")]
    [RequirePermission(Permissions.Inventory.AdjustCreate)]
    public async Task<ActionResult<MovementDto>> CreateAdjustment([FromBody] CreateAdjustmentCommand command, CancellationToken ct)
        => Ok(await Sender.Send(command, ct));

    [HttpGet("kardex")]
    [RequirePermission(Permissions.Inventory.Read)]
    public async Task<ActionResult<IReadOnlyList<KardexEntryDto>>> Kardex(
        [FromQuery] Guid productId, [FromQuery] Guid warehouseId,
        [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, CancellationToken ct)
        => Ok(await Sender.Send(new GetKardexQuery(productId, warehouseId, fromUtc, toUtc), ct));

    [HttpGet("stock")]
    [RequirePermission(Permissions.Inventory.Read)]
    public async Task<ActionResult<IReadOnlyList<StockDto>>> Stock(
        [FromQuery] Guid? productId, [FromQuery] Guid? warehouseId, CancellationToken ct)
        => Ok(await Sender.Send(new GetStockQuery(productId, warehouseId), ct));
}
