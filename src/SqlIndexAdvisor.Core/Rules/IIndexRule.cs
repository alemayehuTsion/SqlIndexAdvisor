using SqlIndexAdvisor.Core.Collection;

namespace SqlIndexAdvisor.Core.Rules;

public interface IIndexRule
{
    string Id { get; }

    IEnumerable<Finding> Evaluate(DatabaseSnapshot snapshot, RuleOptions options);
}
