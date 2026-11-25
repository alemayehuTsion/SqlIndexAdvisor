namespace SqlIndexAdvisor.Core.Advice;

public enum AdviceSource
{
    Model,
    Deterministic,
}

public sealed record AdviceResult(
    AdviceSource Source,
    IReadOnlyList<Assessment> Assessments,
    IReadOnlyList<AdviceIssue> Issues,
    string? ModelId);
