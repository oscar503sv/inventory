using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.API.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    private ISender? _sender;

    protected ISender Sender => _sender ??= HttpContext.RequestServices.GetRequiredService<ISender>();
}
