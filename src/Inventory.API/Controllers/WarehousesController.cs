using Inventory.Application.Features.Warehouses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Authorization;
using Shared.Authorization.AspNetCore;
using Shared.Contracts.Warehouses;

namespace Inventory.API.Controllers;

[Authorize]
[Route("api/v1/warehouses")]
public sealed class WarehousesController : ApiControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Inventory.Read)]
    public async Task<ActionResult<IReadOnlyList<WarehouseDto>>> List(CancellationToken ct)
        => Ok(await Sender.Send(new GetWarehousesQuery(), ct));

    // Warehouse administration is a system-admin task (PRD lacks a dedicated permission).
    [HttpPost]
    [RequirePermission(Permissions.Users.Manage)]
    public async Task<ActionResult<WarehouseDto>> Create([FromBody] CreateWarehouseCommand command, CancellationToken ct)
        => Ok(await Sender.Send(command, ct));

    public sealed record UpdateWarehouseRequest(string Name, string Address);

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.Users.Manage)]
    public async Task<ActionResult<WarehouseDto>> Update(
        Guid id, [FromBody] UpdateWarehouseRequest request, CancellationToken ct)
        => Ok(await Sender.Send(new UpdateWarehouseCommand(id, request.Name, request.Address), ct));
}
