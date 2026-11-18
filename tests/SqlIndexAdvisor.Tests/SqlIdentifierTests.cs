using SqlIndexAdvisor.Core;

namespace SqlIndexAdvisor.Tests;

public class SqlIdentifierTests
{
    [Theory]
    [InlineData("Customers", "[Customers]")]
    [InlineData("Order Details", "[Order Details]")]
    [InlineData("weird]name", "[weird]]name]")]
    [InlineData("[already]", "[[already]]]")]
    public void Quote_WrapsNameInBracketsAndEscapesClosingBrackets(string name, string expected)
    {
        Assert.Equal(expected, SqlIdentifier.Quote(name));
    }

    [Fact]
    public void Quote_AcceptsNameAtMaxLength()
    {
        var name = new string('a', SqlIdentifier.MaxLength);

        Assert.Equal($"[{name}]", SqlIdentifier.Quote(name));
    }

    [Fact]
    public void Quote_RejectsNameOverMaxLength()
    {
        var name = new string('a', SqlIdentifier.MaxLength + 1);

        Assert.Throws<ArgumentException>(() => SqlIdentifier.Quote(name));
    }

    [Fact]
    public void Quote_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => SqlIdentifier.Quote(null!));
    }

    [Fact]
    public void Quote_RejectsEmpty()
    {
        Assert.Throws<ArgumentException>(() => SqlIdentifier.Quote(""));
    }
}
