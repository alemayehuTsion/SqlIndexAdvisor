using System.Globalization;

using SqlIndexAdvisor.Core.Rules;

namespace SqlIndexAdvisor.Tests.Rules;

public class EvidenceTests
{
    [Fact]
    public void From_FormatsNumbersTheSameWayRegardlessOfCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            Assert.Equal("1500000", Evidence.From("rowWrites", 1_500_000L).Value);
            Assert.Equal("95.97", Evidence.From("impactPercent", 95.96999).Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void RuleOptions_DefaultsAreConservative()
    {
        var options = new RuleOptions();

        Assert.Equal(TimeSpan.FromDays(7), options.MinimumStatisticsWindow);
        Assert.Equal(10, options.MinimumMissingIndexSeeks);
    }
}
