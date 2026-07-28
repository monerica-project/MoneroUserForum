using Microsoft.AspNetCore.Mvc;
using MoneroUserForum.Web.Support;

namespace MoneroUserForum.Web.ViewComponents;

public class SponsorsViewComponent : ViewComponent
{
    private readonly SponsorService sponsors;

    public SponsorsViewComponent(SponsorService sponsors)
    {
        this.sponsors = sponsors;
    }

    public async Task<IViewComponentResult> InvokeAsync(bool side = false, int count = 5)
    {
        var list = await this.sponsors.GetAsync(count);
        return this.View(side ? "Side" : "Default", list);
    }
}
