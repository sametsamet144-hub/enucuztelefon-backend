using EnUcuzTelefon.Shared.Results;
using Microsoft.AspNetCore.Mvc;

namespace EnUcuzTelefon.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public abstract class BaseApiController : ControllerBase
{
    protected ActionResult HandleResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return result.Error.Code switch
        {
            var c when c.Contains("NotFound") => NotFound(new { error = result.Error.Message, code = result.Error.Code }),
            var c when c.Contains("Validation") => BadRequest(new { error = result.Error.Message, code = result.Error.Code }),
            var c when c.Contains("Conflict") => Conflict(new { error = result.Error.Message, code = result.Error.Code }),
            _ => StatusCode(500, new { error = result.Error.Message, code = result.Error.Code })
        };
    }
}
