using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1;

[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public abstract class BaseController : ControllerBase
{
    private ISender? _mediator;
    protected ISender Mediator => _mediator ??= HttpContext.RequestServices.GetRequiredService<ISender>();

    protected string CurrentUserId =>
        HttpContext.RequestServices.GetRequiredService<ICurrentUserService>().UserId
        ?? throw new UnauthorizedAccessException("User is not authenticated.");

    protected IActionResult Ok<T>(T data, PagedMeta? meta = null)
        => base.Ok(ApiResponse<T>.Ok(data, meta));

    protected IActionResult Created<T>(T data)
        => StatusCode(201, ApiResponse<T>.Ok(data));

    protected new IActionResult NoContent()
        => StatusCode(204);

    protected IActionResult Fail(string code, string message, int statusCode = 400,
        Dictionary<string, string[]>? fields = null)
        => StatusCode(statusCode, ApiResponse.Fail(code, message, fields));

    protected IActionResult NotFound(string code, string message)
        => Fail(code, message, 404);

    protected IActionResult Conflict(string code, string message)
        => Fail(code, message, 409);

    protected IActionResult Unprocessable(string code, string message,
        Dictionary<string, string[]>? fields = null)
        => StatusCode(422, ApiResponse.Fail(code, message, fields));
}
