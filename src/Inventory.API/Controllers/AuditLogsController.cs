using Inventory.Application.Features.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Authorization;
using Shared.Authorization.AspNetCore;
using Shared.Contracts.Audit;
using Shared.Contracts.Common;

namespace Inventory.API.Controllers;

[Authorize]
[RequirePermission(Permissions.Audit.Read)]
[Route("api/v1/audit-logs")]
public sealed class AuditLogsController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AuditLogDto>>> List([FromQuery] GetAuditLogsQuery query, CancellationToken ct)
        => Ok(await Sender.Send(query, ct));
}
