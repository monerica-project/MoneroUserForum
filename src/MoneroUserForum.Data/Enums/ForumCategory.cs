using System.ComponentModel;

namespace MoneroUserForum.Data.Enums;

/// <summary>
/// Discussion categories a thread can belong to. Editable over time — add new
/// members here and they appear everywhere the enum is rendered (via the
/// <see cref="DescriptionAttribute"/> label). 0 is reserved as Unknown.
/// </summary>
public enum ForumCategory
{
    Unknown = 0,

    [Description("Price Talk")]
    PriceTalk = 1,

    [Description("Technical Help")]
    TechnicalHelp = 2,

    [Description("General Discussion")]
    GeneralDiscussion = 3,

    [Description("Announcements")]
    Announcements = 4,

    [Description("Exchange Talk")]
    ExchangeTalk = 5,
}
