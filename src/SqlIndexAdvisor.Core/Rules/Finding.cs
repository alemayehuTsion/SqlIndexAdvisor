using SqlIndexAdvisor.Core.Collection;

namespace SqlIndexAdvisor.Core.Rules;

public sealed record Finding(
    string RuleId,
    Severity Severity,
    QualifiedName Table,
    string Subject,
    string Summary,
    IReadOnlyList<Evidence> Evidence,
    IndexChange? ProposedChange)
{
    public string Id => $"{RuleId}:{Table.Schema}.{Table.Name}:{Subject}";
}
