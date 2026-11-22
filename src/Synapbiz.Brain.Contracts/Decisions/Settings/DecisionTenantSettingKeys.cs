namespace Nuvora.Decisions.Contracts.Settings;

/// <summary>Keys of the tenant settings the decision engine owns, readable by host modules.</summary>
public static class DecisionTenantSettingKeys
{
    /// <summary>The tenant setting that lets the submitter or an editor approve their own version.</summary>
    public const string AllowSelfApproval = "decisions.allowSelfApproval";
}
