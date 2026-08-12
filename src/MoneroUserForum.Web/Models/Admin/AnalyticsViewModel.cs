namespace MoneroUserForum.Web.Models.Admin;

/// <summary>One day's activity totals for the admin analytics charts.</summary>
public class AnalyticsPoint
{
    public DateOnly Day { get; set; }

    /// <summary>New threads started that day (opening posts).</summary>
    public int Posts { get; set; }

    /// <summary>Replies posted that day (non-opening posts).</summary>
    public int Replies { get; set; }

    /// <summary>Unique thread views recorded that day.</summary>
    public int Views { get; set; }
}

public class AnalyticsViewModel
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public List<AnalyticsPoint> Points { get; set; } = new();

    public int TotalPosts { get; set; }
    public int TotalReplies { get; set; }
    public int TotalViews { get; set; }

    public int Days => this.Points.Count;
}
