using System.Text.Json;

using SqlIndexAdvisor.Core.Rules;

namespace SqlIndexAdvisor.Core.Advice;

// All or nothing: a reply with any problem is rejected as a whole, because a model that ignored one rule has
// not earned partial trust. The caller then falls back to the deterministic ranking.
public static class AdvisorResponseParser
{
    public const int MaxTextLength = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static AdviceParseResult Parse(string reply, IReadOnlyList<Finding> findings)
    {
        ResponseDto? response;
        try
        {
            response = JsonSerializer.Deserialize<ResponseDto>(StripCodeFence(reply), JsonOptions);
        }
        catch (JsonException exception)
        {
            return AdviceParseResult.Rejected(new AdviceIssue("invalid-json", $"The reply is not valid JSON: {exception.Message}"));
        }

        if (response?.Assessments is not { Count: > 0 } assessmentsOrNulls)
        {
            return AdviceParseResult.Rejected(new AdviceIssue("no-assessments", "The reply contains no assessments."));
        }

        if (assessmentsOrNulls.Any(dto => dto is null))
        {
            return AdviceParseResult.Rejected(new AdviceIssue("null-assessment", "The reply contains an empty (null) assessment."));
        }

        var assessments = assessmentsOrNulls.Cast<AssessmentDto>().ToList();

        var issues = new List<AdviceIssue>();
        issues.AddRange(CheckFindingIds(assessments, findings));
        issues.AddRange(CheckRanks(assessments, findings.Count));
        issues.AddRange(CheckTexts(assessments));

        if (issues.Count > 0)
        {
            return new AdviceParseResult([], issues);
        }

        return new AdviceParseResult(
            assessments
                .Select(dto => new Assessment(dto.FindingId!, dto.Rank, dto.Explanation!.Trim(), dto.Risks?.Trim() ?? ""))
                .OrderBy(assessment => assessment.Rank)
                .ToList(),
            []);
    }

    private static IEnumerable<AdviceIssue> CheckFindingIds(IReadOnlyList<AssessmentDto> assessments, IReadOnlyList<Finding> findings)
    {
        var knownIds = findings.Select(finding => finding.Id).ToHashSet(StringComparer.Ordinal);
        var returnedIds = assessments.Select(dto => dto.FindingId ?? "").ToList();

        foreach (var id in returnedIds.Where(id => !knownIds.Contains(id)).Distinct())
        {
            yield return new AdviceIssue("unknown-finding", $"The reply refers to '{id}', which is not one of the findings.");
        }

        foreach (var id in returnedIds.GroupBy(id => id, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key))
        {
            yield return new AdviceIssue("duplicate-finding", $"The reply assesses '{id}' more than once.");
        }

        foreach (var id in knownIds.Except(returnedIds, StringComparer.Ordinal))
        {
            yield return new AdviceIssue("missing-finding", $"The reply has no assessment for '{id}'.");
        }
    }

    private static IEnumerable<AdviceIssue> CheckRanks(IReadOnlyList<AssessmentDto> assessments, int findingCount)
    {
        var ranks = assessments.Select(dto => dto.Rank).Order().ToList();

        if (!ranks.SequenceEqual(Enumerable.Range(1, findingCount)))
        {
            yield return new AdviceIssue(
                "invalid-ranks", $"Ranks must be 1 to {findingCount} with no gaps or repeats; got {string.Join(", ", ranks)}.");
        }
    }

    private static IEnumerable<AdviceIssue> CheckTexts(IReadOnlyList<AssessmentDto> assessments)
    {
        foreach (var dto in assessments)
        {
            if (string.IsNullOrWhiteSpace(dto.Explanation))
            {
                yield return new AdviceIssue("empty-explanation", $"The assessment of '{dto.FindingId}' has no explanation.");
            }

            if ((dto.Explanation?.Length ?? 0) > MaxTextLength || (dto.Risks?.Length ?? 0) > MaxTextLength)
            {
                yield return new AdviceIssue(
                    "text-too-long", $"The assessment of '{dto.FindingId}' exceeds {MaxTextLength} characters.");
            }
        }
    }

    private static string StripCodeFence(string reply)
    {
        var text = reply.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal) || !text.EndsWith("```", StringComparison.Ordinal))
        {
            return text;
        }

        var firstLineEnd = text.IndexOf('\n');
        return firstLineEnd < 0 ? text : text[(firstLineEnd + 1)..^3].Trim();
    }

    private sealed record ResponseDto(List<AssessmentDto?>? Assessments);

    private sealed record AssessmentDto(string? FindingId, int Rank, string? Explanation, string? Risks);
}
