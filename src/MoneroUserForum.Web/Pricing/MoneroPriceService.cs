namespace MoneroUserForum.Web.Pricing;

/// <summary>
/// Holds the latest XMR price fetched from moneropricenow.com. Populated by
/// <see cref="MoneroPriceRefresher"/> (server-side, every minute) and read by the
/// price view component — so the price renders server-side and works without JavaScript.
/// </summary>
public sealed class MoneroPriceService
{
    private volatile Snapshot? current;

    public Snapshot? Current => this.current;

    public void Set(decimal priceUsdt, DateTime asOfUtc) =>
        this.current = new Snapshot(priceUsdt, asOfUtc);

    public sealed record Snapshot(decimal PriceUsdt, DateTime AsOfUtc);
}
