namespace SqlIndexAdvisor.Core.Collection;

internal static class SqlScript
{
    public static string Load(string name)
    {
        var resourceName = $"Sql.{name}.sql";
        using var stream = typeof(SqlScript).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded SQL script '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
