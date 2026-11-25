using System.Text.Json;

using SqlIndexAdvisor.Core.Rules;
using SqlIndexAdvisor.Core.Validation;

namespace SqlIndexAdvisor.Core.Advice;

public static class AdvisorPrompt
{
    public static readonly string SystemInstructions =
        $$"""
        You review findings that deterministic rules produced for a SQL Server database. The rules have already
        decided which index changes are proposed; you cannot add, remove or alter a finding or its proposal.
        Each proposal has also been checked by a validator: a rejected proposal will not be offered for approval,
        and warnings describe known risks of applying it.

        For every finding you receive, return exactly one assessment:
        - findingId: the finding's id, copied exactly.
        - rank: a unique number from 1 to the number of findings, where 1 is the finding a database administrator
          should look at first. Weigh the benefit of the proposal against its risk and its validation result,
          using only the evidence given.
        - explanation: two to four plain sentences on why this finding matters, citing only numbers that appear in
          its evidence or summary. Mention it when the proposal was rejected, and why.
        - risks: what could go wrong if the proposed change is applied, including any validation warnings, or an
          empty string when nothing is proposed.

        Keep explanation and risks under {{AdvisorResponseParser.MaxTextLength}} characters each; longer replies are discarded.
        Reply with JSON only, in this shape:
        {"assessments":[{"findingId":"...","rank":1,"explanation":"...","risks":"..."}]}
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string BuildUserMessage(IReadOnlyList<Finding> findings, IReadOnlyList<ProposalValidation> validations) =>
        JsonSerializer.Serialize(
            new
            {
                findings = findings.Select(finding => ToModelInput(
                    finding, validations.FirstOrDefault(validation => validation.Finding.Id == finding.Id))),
            },
            JsonOptions);

    private static object ToModelInput(Finding finding, ProposalValidation? validation) =>
        new
        {
            id = finding.Id,
            severity = finding.Severity.ToString(),
            table = finding.Table.ToString(),
            summary = finding.Summary,
            evidence = finding.Evidence.ToDictionary(evidence => evidence.Name, evidence => evidence.Value),
            proposal = finding.ProposedChange is null ? null : Describe(finding.ProposedChange),
            validation = validation is null
                ? null
                : new
                {
                    accepted = validation.IsAccepted,
                    issues = validation.Issues.Select(issue => new { level = issue.Level.ToString(), code = issue.Code, issue.Message }),
                },
        };

    private static string Describe(IndexChange change) =>
        change switch
        {
            CreateIndex create =>
                $"create nonclustered index {create.IndexName} on {create.Table} " +
                $"({string.Join(", ", create.KeyColumns.Select(key => key.IsDescending ? $"{key.Name} DESC" : key.Name))})" +
                (create.IncludedColumns.Count > 0 ? $" include ({string.Join(", ", create.IncludedColumns)})" : ""),
            DropIndex drop => $"drop index {drop.IndexName} on {drop.Table}",
            _ => throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown index change."),
        };
}
