using System.Text.RegularExpressions;

namespace MoneroUserForum.Web.Support;

/// <summary>
/// Keeps scripted sign-ups and contact messages out of the admin's approval queue. Two parts:
/// wording rules (a join note or username that calls itself a probe, test, audit or QA account,
/// code-injection strings, reserved test email domains) and a site-wide hourly ceiling per
/// action. The ceiling looks at nothing about the visitor: it only counts recent uses.
/// Written after 2026-11-16, when a scanner registered 15 accounts and sent 14 contact messages.
/// </summary>
public sealed partial class SignupGuard
{
    public const int RegistrationsPerHour = 20;
    public const int ContactMessagesPerHour = 20;

    public const string RefusedMessage =
        "That doesn't look like a real request to join. Tell us in your own words why you'd like an account.";

    public const string BusyMessage =
        "A lot of requests are arriving right now. Please try again in a little while.";

    /// <summary>What one visitor may do per hour, so one source can't use up the site-wide number.</summary>
    public const int RegistrationsPerVisitorPerHour = 2;
    public const int ContactMessagesPerVisitorPerHour = 3;

    /// <summary>A person needs at least this long to read the form and type an answer.</summary>
    public static readonly TimeSpan MinimumFillTime = TimeSpan.FromSeconds(4);

    private static readonly string[] Words = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten" };

    private readonly object _gate = new();
    private readonly Dictionary<string, Queue<DateTime>> _recent = new(StringComparer.Ordinal);

    /// <summary>True when the text reads as an automated test or a security probe.</summary>
    public static bool LooksAutomated(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            return ProbeWords().IsMatch(text) || InjectionMarks().IsMatch(text) || GluedMarks().IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>True for addresses on domains reserved for examples and testing.</summary>
    public static bool IsThrowawayTestEmail(string? email)
    {
        var value = (email ?? string.Empty).Trim().ToLowerInvariant();
        var at = value.LastIndexOf('@');
        if (at < 0 || at == value.Length - 1)
        {
            return false;
        }

        var host = value[(at + 1)..];
        return host is "example.com" or "example.org" or "example.net"
            || host.EndsWith(".example.com", StringComparison.Ordinal)
            || host.EndsWith(".example", StringComparison.Ordinal)
            || host.EndsWith(".invalid", StringComparison.Ordinal)
            || host.EndsWith(".test", StringComparison.Ordinal)
            || host.EndsWith(".localhost", StringComparison.Ordinal);
    }

    /// <summary>
    /// A spam-check question written in words and in changing forms ("seven minus two", "the larger
    /// of four and nine"), so a script can't answer by adding the two digits it finds on the page.
    /// </summary>
    public static (string Question, int Answer) NewQuestion()
    {
        var a = Random.Shared.Next(1, 10);
        var b = Random.Shared.Next(1, 10);
        switch (Random.Shared.Next(4))
        {
            case 0:
                return ($"{Words[a]} plus {Words[b]}", a + b);
            case 1:
                var hi = Math.Max(a, b);
                var lo = Math.Min(a, b);
                return ($"{Words[hi]} minus {Words[lo]}", hi - lo);
            case 2:
                if (a == b)
                {
                    b = a == 9 ? 1 : a + 1;
                }

                return ($"the larger of {Words[a]} and {Words[b]}", Math.Max(a, b));
            default:
                if (a == b)
                {
                    b = a == 9 ? 1 : a + 1;
                }

                return ($"the smaller of {Words[a]} and {Words[b]}", Math.Min(a, b));
        }
    }

    /// <summary>The visitor address as nginx saw it; "local" for the onion service. Used only as an in-memory counter key.</summary>
    public static string VisitorKey(HttpContext http)
    {
        var real = http.Request.Headers["X-Real-IP"].ToString();
        var ip = string.IsNullOrWhiteSpace(real) ? http.Connection.RemoteIpAddress?.ToString() : real.Trim();
        return string.IsNullOrWhiteSpace(ip) || ip == "127.0.0.1" || ip == "::1" || ip == "::ffff:127.0.0.1" ? "local" : ip;
    }

    /// <summary>Counts one use for this visitor and for the site; false when either allowance is used up.</summary>
    public bool TryCount(string action, int perHour, string visitorKey, int perVisitor)
    {
        var allowance = visitorKey == "local" ? perVisitor * 3 : perVisitor;
        lock (_gate)
        {
            if (_recent.Count > 5000)
            {
                var now = DateTime.UtcNow;
                foreach (var old in _recent.Where(p => p.Value.Count == 0 || now - p.Value.Last() >= TimeSpan.FromHours(1)).Select(p => p.Key).ToList())
                {
                    _recent.Remove(old);
                }
            }

            return TryCount(action + "|" + visitorKey, allowance) && TryCount(action, perHour);
        }
    }

    /// <summary>Counts one use of <paramref name="action"/>; false once this hour's ceiling is reached.</summary>
    public bool TryCount(string action, int perHour)
    {
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            if (!_recent.TryGetValue(action, out var times))
            {
                times = new Queue<DateTime>();
                _recent[action] = times;
            }

            while (times.Count > 0 && now - times.Peek() >= TimeSpan.FromHours(1))
            {
                times.Dequeue();
            }

            if (times.Count >= perHour)
            {
                return false;
            }

            times.Enqueue(now);
            return true;
        }
    }

    /// <summary>
    /// True when <paramref name="action"/> has already reached its hourly limit for this visitor or
    /// for the whole site. Only looks: nothing is recorded (see <see cref="Record"/>), so callers can
    /// check first and count only the attempts that should count (e.g. failed sign-ins).
    /// </summary>
    public bool IsOver(string action, int perHour, string visitorKey, int perVisitor)
    {
        var allowance = visitorKey == "local" ? perVisitor * 3 : perVisitor;
        lock (_gate)
        {
            return CountRecent(action + "|" + visitorKey) >= allowance || CountRecent(action) >= perHour;
        }
    }

    /// <summary>True when <paramref name="key"/> already has <paramref name="perHour"/> entries in the last hour. Only looks.</summary>
    public bool IsOver(string key, int perHour)
    {
        lock (_gate)
        {
            return CountRecent(key) >= perHour;
        }
    }

    /// <summary>Records one occurrence of <paramref name="action"/> for this visitor and for the whole site.</summary>
    public void Record(string action, string visitorKey)
    {
        lock (_gate)
        {
            Add(action + "|" + visitorKey);
            Add(action);
        }
    }

    /// <summary>
    /// Returns the slot a visitor took with <see cref="TryCount(string, int, string, int)"/>, for
    /// uses that should not count after all (a sign-in that turned out to be correct).
    /// </summary>
    public void GiveBack(string action, string visitorKey)
    {
        lock (_gate)
        {
            foreach (var key in new[] { action + "|" + visitorKey, action })
            {
                if (_recent.TryGetValue(key, out var times) && times.Count > 0)
                {
                    times.Dequeue();
                }
            }
        }
    }

    /// <summary>Records one occurrence under a single key.</summary>
    public void Record(string key)
    {
        lock (_gate)
        {
            Add(key);
        }
    }

    // Callers hold _gate.
    private int CountRecent(string key)
    {
        if (!_recent.TryGetValue(key, out var times))
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        while (times.Count > 0 && now - times.Peek() >= TimeSpan.FromHours(1))
        {
            times.Dequeue();
        }

        return times.Count;
    }

    // Callers hold _gate.
    private void Add(string key)
    {
        // Per-account keys can be many; drop ones idle for an hour so memory stays bounded.
        if (_recent.Count > 5000)
        {
            var now = DateTime.UtcNow;
            foreach (var old in _recent.Where(p => p.Value.Count == 0 || now - p.Value.Last() >= TimeSpan.FromHours(1)).Select(p => p.Key).ToList())
            {
                _recent.Remove(old);
            }
        }

        if (!_recent.TryGetValue(key, out var times))
        {
            times = new Queue<DateTime>();
            _recent[key] = times;
        }

        times.Enqueue(DateTime.UtcNow);
    }

    [GeneratedRegex(@"(?<![a-z0-9])(probe\w*|ssrf|idor|xss|sqli|csrf|pentest\w*|pen\s*test\w*|enumeration|mass\s*assign\w*|injection|security[\s-]+(assessment|audit|research\w*|test\w*)|authori[sz]ed\s+(assessment|test\w*|audit)|qa\s+(tester|test|probe|account)|test\s+account|audit\s*\d{2,}|qa\s*\d{2,})(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex ProbeWords();

    // Markers that show up glued inside usernames ("zzprobe9k", "sqli1390", "massassign1390").
    [GeneratedRegex(@"probe|sqli|massassign|pentest|ssrf|idor\d|qa\d{3,}|audit\d{2,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex GluedMarks();

    [GeneratedRegex(@"<\s*/?\s*[a-z][^>]*>|\{\{.*?\}\}|\$\{[^}]*\}|'\s*(or|and)\s+\d|;\s*(select|drop|insert|update)\s|(?<![a-z])(pg_sleep|sleep)\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline, 250)]
    private static partial Regex InjectionMarks();
}
