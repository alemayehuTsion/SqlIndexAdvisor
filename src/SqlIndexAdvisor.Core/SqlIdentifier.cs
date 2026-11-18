namespace SqlIndexAdvisor.Core;

public static class SqlIdentifier
{
    public const int MaxLength = 128;

    public static string Quote(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (name.Length > MaxLength)
        {
            throw new ArgumentException(
                $"SQL Server identifiers are limited to {MaxLength} characters; got {name.Length}.",
                nameof(name));
        }

        return "[" + name.Replace("]", "]]") + "]";
    }
}
