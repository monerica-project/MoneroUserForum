using Microsoft.AspNetCore.Mvc;

namespace MoneroUserForum.Web.Pricing;

/// <summary>Renders the current XMR price (server-side, no JavaScript). Renders nothing
/// until the first successful fetch.</summary>
public sealed class MoneroPriceViewComponent : ViewComponent
{
    private readonly MoneroPriceService service;

    public MoneroPriceViewComponent(MoneroPriceService service) => this.service = service;

    public IViewComponentResult Invoke() => this.View(this.service.Current);
}
