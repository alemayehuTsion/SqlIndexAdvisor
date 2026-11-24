using SqlIndexAdvisor.Core.Collection;

namespace SqlIndexAdvisor.Core.Rules;

internal static class IndexNaming
{
    public static string ProposeName(DatabaseSnapshot snapshot, QualifiedName table, IEnumerable<string> keyColumns)
    {
        var baseName = $"IX_{table.Name}_{string.Join("_", keyColumns)}";
        var existingNames = snapshot.Indexes
            .Where(index => index.Table == table)
            .Select(index => index.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var name = baseName;
        for (var suffix = 2; existingNames.Contains(name); suffix++)
        {
            name = $"{baseName}_{suffix}";
        }

        return name;
    }
}
