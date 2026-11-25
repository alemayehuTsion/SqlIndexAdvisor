using SqlIndexAdvisor.Core.Collection;
using SqlIndexAdvisor.Core.Rules;

namespace SqlIndexAdvisor.Core.Validation;

// Re-checks every proposed change against the snapshot without reusing rule logic, so a bug in a rule cannot
// switch off the check that should catch it.
public sealed class ProposalValidator
{
    public IReadOnlyList<ProposalValidation> Validate(DatabaseSnapshot snapshot, IReadOnlyList<Finding> findings)
    {
        var proposals = findings.Where(finding => finding.ProposedChange is not null).ToList();

        return proposals
            .Select(finding => new ProposalValidation(finding, Check(snapshot, proposals, finding.ProposedChange!).ToList()))
            .ToList();
    }

    private static IEnumerable<ValidationIssue> Check(DatabaseSnapshot snapshot, IReadOnlyList<Finding> proposals, IndexChange change)
    {
        if (!snapshot.Columns.Any(column => column.Table == change.Table))
        {
            return [ValidationIssue.Error("table-not-found", $"Table {change.Table} does not exist in {snapshot.DatabaseName}.")];
        }

        return change switch
        {
            CreateIndex create => CheckCreate(snapshot, proposals, create),
            DropIndex drop => CheckDrop(snapshot, drop),
            _ => [ValidationIssue.Error("unknown-change", $"Unsupported change type {change.GetType().Name}.")],
        };
    }

    private static IEnumerable<ValidationIssue> CheckCreate(
        DatabaseSnapshot snapshot, IReadOnlyList<Finding> proposals, CreateIndex create)
    {
        var tableColumns = snapshot.Columns
            .Where(column => column.Table == create.Table)
            .Select(column => column.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var column in ColumnsOf(create).Where(column => !tableColumns.Contains(column)))
        {
            yield return ValidationIssue.Error("column-not-found", $"Column {column} does not exist on {create.Table}.");
        }

        foreach (var issue in CheckName(snapshot, proposals, create))
        {
            yield return issue;
        }
    }

    private static IEnumerable<ValidationIssue> CheckName(
        DatabaseSnapshot snapshot, IReadOnlyList<Finding> proposals, CreateIndex create)
    {
        if (string.IsNullOrWhiteSpace(create.IndexName))
        {
            yield return ValidationIssue.Error("name-empty", "The proposed index has no name.");
            yield break;
        }

        if (create.IndexName.Length > SqlIdentifier.MaxLength)
        {
            yield return ValidationIssue.Error(
                "name-too-long",
                $"{create.IndexName} is {create.IndexName.Length} characters; SQL Server allows {SqlIdentifier.MaxLength}.");
        }

        if (snapshot.Indexes.Any(index => index.Table == create.Table && SameName(index.Name, create.IndexName)))
        {
            yield return ValidationIssue.Error("name-taken", $"An index named {create.IndexName} already exists on {create.Table}.");
        }

        var proposalsWithSameName = proposals
            .Select(finding => finding.ProposedChange)
            .OfType<CreateIndex>()
            .Count(other => other.Table == create.Table && SameName(other.IndexName, create.IndexName));
        if (proposalsWithSameName > 1)
        {
            yield return ValidationIssue.Error(
                "name-not-unique", $"{proposalsWithSameName} proposals would create an index named {create.IndexName} on {create.Table}.");
        }
    }

    private static IEnumerable<ValidationIssue> CheckDrop(DatabaseSnapshot snapshot, DropIndex drop)
    {
        if (!snapshot.Indexes.Any(index => index.Table == drop.Table && SameName(index.Name, drop.IndexName)))
        {
            yield return ValidationIssue.Error("index-not-found", $"There is no index {drop.IndexName} on {drop.Table} to drop.");
        }
    }

    private static IEnumerable<string> ColumnsOf(CreateIndex create) =>
        create.KeyColumns.Select(key => key.Name).Concat(create.IncludedColumns);

    private static bool SameName(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
}
