using SqlIndexAdvisor.Core.Collection;

namespace SqlIndexAdvisor.Core.Rules;

public sealed record Finding(
    string RuleId,
    Severity Severity,
    QualifiedName Table,
    string Summary,
    IReadOnlyList<Evidence> Evidence,
    IndexChange? ProposedChange);
