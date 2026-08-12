using WitcherHub.Configuration.ModelBinding;

namespace WitcherHub.Tests;

public class FlexibleDecimalParsingTests
{
    [Theory]
    // German notation — the reported "Base Price 0,00 is invalid" defect.
    [InlineData("0,00", 0)]
    [InlineData("1234,56", 1234.56)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1.234.567,89", 1234567.89)]
    [InlineData("-99,95", -99.95)]
    // Invariant notation, which the JavaScript on the page posts.
    [InlineData("0.00", 0)]
    [InlineData("1234.56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("1234", 1234)]
    [InlineData("-99.95", -99.95)]
    // Values users paste from documents.
    [InlineData(" 49,90 €", 49.90)]
    [InlineData("1 234,50", 1234.50)]
    public void ParsesBothGermanAndInvariantNotation(string input, decimal expected)
    {
        Assert.True(FlexibleDecimalModelBinder.TryParse(input, out var parsed), $"Failed to parse '{input}'.");
        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("12,34,56")]
    [InlineData("€")]
    public void RejectsValuesThatAreNotNumbers(string input)
    {
        Assert.False(FlexibleDecimalModelBinder.TryParse(input, out _));
    }

    [Fact]
    public void TreatsASingleCommaBeforeThreeDigitsAsAThousandsSeparator()
    {
        // "1,234" is genuinely ambiguous. Money has two decimals, so the
        // thousands reading is the safer default and matches invariant input.
        Assert.True(FlexibleDecimalModelBinder.TryParse("1,234", out var parsed));
        Assert.Equal(1234m, parsed);
    }
}
