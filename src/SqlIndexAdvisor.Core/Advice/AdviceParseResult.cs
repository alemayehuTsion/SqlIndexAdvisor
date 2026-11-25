namespace SqlIndexAdvisor.Core.Advice;

public sealed record AdviceIssue(string Code, string Message);

public sealed record AdviceParseResult(IReadOnlyList<Assessment> Assessments, IReadOnlyList<AdviceIssue> Issues)
{
    public bool IsUsable => Issues.Count == 0;

    public static AdviceParseResult Rejected(params AdviceIssue[] issues) => new([], issues);
}
