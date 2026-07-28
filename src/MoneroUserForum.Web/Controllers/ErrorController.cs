using Microsoft.AspNetCore.Mvc;

namespace MoneroUserForum.Web.Controllers;

[Route("error")]
public class ErrorController : Controller
{
    [Route("{code:int?}")]
    public IActionResult Index(int? code)
    {
        this.Response.StatusCode = code ?? 500;
        this.ViewBag.Code = code ?? 500;
        return this.View();
    }
}
