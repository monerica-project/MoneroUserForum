using System.ComponentModel;

namespace MoneroUserForum.Data.Enums;

/// <summary>
/// A member's standing, derived from account age + activity (see
/// <c>ReputationCalculator</c>). Editable over time — more levels can be added.
/// Three logical tiers for now.
/// </summary>
public enum ReputationLevel
{
    [Description("New User")]
    NewUser = 0,

    [Description("Member")]
    Member = 1,

    [Description("Veteran")]
    Veteran = 2,
}
