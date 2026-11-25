using SqlIndexAdvisor.Core.Rules;

namespace SqlIndexAdvisor.Core.Validation;

public sealed record ProposalValidation(Finding Finding, IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsAccepted => Issues.All(issue => issue.Level != ValidationLevel.Error);
}
