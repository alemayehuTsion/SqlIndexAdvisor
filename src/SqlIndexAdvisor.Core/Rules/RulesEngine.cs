using SqlIndexAdvisor.Core.Collection;

namespace SqlIndexAdvisor.Core.Rules;

public sealed class RulesEngine(IReadOnlyList<IIndexRule> rules)
{
    public static RulesEngine Default { get; } = new(
    [
        new MissingIndexRule(),
        new UnusedIndexRule(),
        new DuplicateIndexRule(),
        new UnindexedForeignKeyRule(),
    ]);

    public IReadOnlyList<Finding> Evaluate(DatabaseSnapshot snapshot, RuleOptions options)
    {
        var findings = rules.SelectMany(rule => rule.Evaluate(snapshot, options)).ToList();

        var ordered = Consolidate(findings)
            .OrderByDescending(finding => finding.Severity)
            .ThenBy(finding => finding.Table.ToString(), StringComparer.Ordinal)
            .ThenBy(finding => finding.RuleId, StringComparer.Ordinal);

        return WithUniqueIds(ordered).ToList();
    }

    // The AI and the audit log refer to findings by Id, so a run must never contain two equal Ids. A clash means
    // a rule produced two findings with the same subject; suffixing keeps the run usable and the Ids deterministic.
    private static IEnumerable<Finding> WithUniqueIds(IEnumerable<Finding> findings)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var finding in findings)
        {
            var occurrence = seen[finding.Id] = seen.GetValueOrDefault(finding.Id) + 1;
            yield return occurrence == 1 ? finding : finding with { Subject = $"{finding.Subject}#{occurrence}" };
        }
    }

    // Two rules can propose the same change, or one proposal can make another unnecessary. Proposals are visited
    // widest first and only compared with proposals already kept, so resolvedBy always names a surviving change.
    private static IEnumerable<Finding> Consolidate(IReadOnlyList<Finding> findings)
    {
        var kept = new List<Finding>();

        foreach (var finding in findings.Where(finding => finding.ProposedChange is null))
        {
            yield return finding;
        }

        foreach (var finding in findings.Where(finding => finding.ProposedChange is not null).OrderBy(Preference))
        {
            var survivor = kept.FirstOrDefault(other => Covers(other.ProposedChange!, finding.ProposedChange!));
            if (survivor is null)
            {
                kept.Add(finding);
                yield return finding;
            }
            else
            {
                yield return ResolvedBy(finding, survivor);
            }
        }
    }

    private static (int, int, int, string, string) Preference(Finding finding) =>
        finding.ProposedChange switch
        {
            CreateIndex create => (-create.KeyColumns.Count, -create.IncludedColumns.Count, -(int)finding.Severity,
                finding.RuleId, create.IndexName),
            _ => (0, 0, -(int)finding.Severity, finding.RuleId, finding.ProposedChange!.IndexName),
        };

    private static bool Covers(IndexChange wider, IndexChange narrower) =>
        (wider, narrower) switch
        {
            (DropIndex a, DropIndex b) => a == b,
            (CreateIndex a, CreateIndex b) =>
                a.Table == b.Table
                && IndexCoverage.LeadsWith(a.KeyColumns, [.. b.KeyColumns.Select(key => key.Name)])
                && b.IncludedColumns.All(column => ColumnsOf(a).Contains(column, StringComparer.OrdinalIgnoreCase)),
            _ => false,
        };

    private static IEnumerable<string> ColumnsOf(CreateIndex index) =>
        index.KeyColumns.Select(key => key.Name).Concat(index.IncludedColumns);

    private static Finding ResolvedBy(Finding finding, Finding survivor)
    {
        var survivorChange = survivor.ProposedChange!;

        return finding with
        {
            Summary = $"{finding.Summary} Covered by the {survivor.RuleId} proposal for {survivorChange.IndexName}.",
            Evidence = [.. finding.Evidence, Evidence.From("resolvedBy", survivorChange.IndexName)],
            ProposedChange = null,
        };
    }
}
