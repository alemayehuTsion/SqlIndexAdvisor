using System.Globalization;

using SqlIndexAdvisor.Core.Rules;

namespace SqlIndexAdvisor.Cli;

internal sealed record CliArguments(RuleOptions RuleOptions, string? AiProvider)
{
    public const string Usage = "Options: --min-window-hours <n>, --min-seeks <n>, --ai <None|Anthropic>";

    public static CliArguments Parse(string[] arguments)
    {
        var parsed = new CliArguments(new RuleOptions(), AiProvider: null);

        for (var i = 0; i < arguments.Length; i++)
        {
            parsed = arguments[i] switch
            {
                "--min-window-hours" => parsed with
                {
                    RuleOptions = parsed.RuleOptions with { MinimumStatisticsWindow = TimeSpan.FromHours(Number(arguments, ++i)) },
                },
                "--min-seeks" => parsed with
                {
                    RuleOptions = parsed.RuleOptions with { MinimumMissingIndexSeeks = (long)Number(arguments, ++i) },
                },
                "--ai" => parsed with { AiProvider = Value(arguments, ++i) },
                _ => throw new ArgumentException($"Unknown option '{arguments[i]}'. {Usage}"),
            };
        }

        return parsed;
    }

    private static string Value(string[] arguments, int position) =>
        position < arguments.Length
            ? arguments[position]
            : throw new ArgumentException($"Option '{arguments[position - 1]}' needs a value.");

    private static double Number(string[] arguments, int position) =>
        double.TryParse(Value(arguments, position), CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value
            : throw new ArgumentException($"Option '{arguments[position - 1]}' needs a non-negative number.");
}
