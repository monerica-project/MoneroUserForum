using Microsoft.AspNetCore.Mvc;
using MoneroUserForum.Web.Support;

namespace MoneroUserForum.Web.Controllers;

[Route("sponsors")]
public class SponsorsController : Controller
{
    private readonly SponsorService sponsors;

    public SponsorsController(SponsorService sponsors)
    {
        this.sponsors = sponsors;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
        => this.View(await this.sponsors.GetAllAsync());
}
