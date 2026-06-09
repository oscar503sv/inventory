using Inventory.Application.Features.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Authorization;
using Shared.Authorization.AspNetCore;
using Shared.Contracts.Catalog;
using Shared.Contracts.Common;

namespace Inventory.API.Controllers;

[Authorize]
[Route("api/v1/products")]
public sealed class ProductsController : ApiControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Products.Read)]
    public async Task<ActionResult<PagedResult<ProductDto>>> List(
        [FromQuery] string? search, [FromQuery] bool? isActive,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await Sender.Send(new GetProductsQuery(search, isActive, page, pageSize), ct));

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.Products.Read)]
    public async Task<ActionResult<ProductDto>> Get(Guid id, CancellationToken ct)
        => Ok(await Sender.Send(new GetProductByIdQuery(id), ct));

    [HttpPost]
    [RequirePermission(Permissions.Products.Create)]
    public async Task<ActionResult<ProductDto>> Create([FromBody] CreateProductCommand command, CancellationToken ct)
    {
        var product = await Sender.Send(command, ct);
        return CreatedAtAction(nameof(Get), new { id = product.Id }, product);
    }

    public sealed record UpdateProductRequest(
        string Name, string Description, Guid CategoryId, Guid BrandId, Guid UnitOfMeasureId, decimal MinimumStock);

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.Products.Update)]
    public async Task<ActionResult<ProductDto>> Update(
        Guid id, [FromBody] UpdateProductRequest request, CancellationToken ct)
        => Ok(await Sender.Send(new UpdateProductCommand(
            id, request.Name, request.Description, request.CategoryId,
            request.BrandId, request.UnitOfMeasureId, request.MinimumStock), ct));

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.Products.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await Sender.Send(new DeleteProductCommand(id), ct);
        return NoContent();
    }
}
