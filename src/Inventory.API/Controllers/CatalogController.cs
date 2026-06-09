using Inventory.Application.Features.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Authorization;
using Shared.Authorization.AspNetCore;
using Shared.Contracts.Catalog;

namespace Inventory.API.Controllers;

[Authorize]
[Route("api/v1/categories")]
public sealed class CategoriesController : ApiControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Products.Read)]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> List(CancellationToken ct)
        => Ok(await Sender.Send(new GetCategoriesQuery(), ct));

    [HttpPost]
    [RequirePermission(Permissions.Products.Create)]
    public async Task<ActionResult<CategoryDto>> Create([FromBody] CreateCategoryCommand command, CancellationToken ct)
        => Ok(await Sender.Send(command, ct));
}

[Authorize]
[Route("api/v1/brands")]
public sealed class BrandsController : ApiControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Products.Read)]
    public async Task<ActionResult<IReadOnlyList<BrandDto>>> List(CancellationToken ct)
        => Ok(await Sender.Send(new GetBrandsQuery(), ct));

    [HttpPost]
    [RequirePermission(Permissions.Products.Create)]
    public async Task<ActionResult<BrandDto>> Create([FromBody] CreateBrandCommand command, CancellationToken ct)
        => Ok(await Sender.Send(command, ct));
}

[Authorize]
[Route("api/v1/units-of-measure")]
public sealed class UnitsOfMeasureController : ApiControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Products.Read)]
    public async Task<ActionResult<IReadOnlyList<UnitOfMeasureDto>>> List(CancellationToken ct)
        => Ok(await Sender.Send(new GetUnitsOfMeasureQuery(), ct));

    [HttpPost]
    [RequirePermission(Permissions.Products.Create)]
    public async Task<ActionResult<UnitOfMeasureDto>> Create([FromBody] CreateUnitOfMeasureCommand command, CancellationToken ct)
        => Ok(await Sender.Send(command, ct));
}
