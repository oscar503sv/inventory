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
[Route("api/v1/inventory/movements")]
public sealed class MovementsController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<MovementSummaryDto>>> List([FromQuery] GetMovementsQuery query, CancellationToken ct)
        => Ok(await Sender.Send(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MovementDto>> Detail(Guid id, CancellationToken ct)
        => Ok(await Sender.Send(new GetMovementByIdQuery(id), ct));
}
