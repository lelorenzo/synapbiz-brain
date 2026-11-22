using Nuvora.Decisions.Contracts.Tables;

namespace Nuvora.Decisions.Contracts.Validation;

/// <summary>Optional module validation of an outcome together with its row conditions.</summary>
public interface IDecisionRowValidator
{
    /// <summary>
    /// Returns issues for <paramref name="row"/>. The engine supplies default rows
    /// with empty conditions because a default applies regardless of its authored conditions.
    /// </summary>
    IReadOnlyList<ValidationIssue> ValidateRow(Row row);
}
