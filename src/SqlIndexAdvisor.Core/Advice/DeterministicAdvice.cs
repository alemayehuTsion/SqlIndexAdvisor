using SqlIndexAdvisor.Core.Rules;
using SqlIndexAdvisor.Core.Validation;

namespace SqlIndexAdvisor.Core.Advice;

public static class DeterministicAdvice
{
    public static AdviceResult For(
        IReadOnlyList<Finding> findings,
        IReadOnlyList<ProposalValidation> validations,
        IReadOnlyList<AdviceIssue> reasons)
    {
        var assessments = findings
            .Select((finding, index) => new Assessment(
                finding.Id,
                index + 1,
                finding.Summary,
                RisksFrom(validations.FirstOrDefault(validation => validation.Finding.Id == finding.Id))))
            .ToList();

        return new AdviceResult(AdviceSource.Deterministic, assessments, reasons, ModelId: null);
    }

    private static string RisksFrom(ProposalValidation? validation) =>
        validation is null ? "" : string.Join(" ", validation.Issues.Select(issue => $"{issue.Level}: {issue.Message}"));
}
