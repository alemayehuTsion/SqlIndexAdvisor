using System.Globalization;

namespace SqlIndexAdvisor.Core.Rules;

public sealed record Evidence(string Name, string Value)
{
    public static Evidence From(string name, long value) =>
        new(name, value.ToString(CultureInfo.InvariantCulture));

    public static Evidence From(string name, double value) =>
        new(name, value.ToString("0.##", CultureInfo.InvariantCulture));

    public static Evidence From(string name, string value) => new(name, value);
}
