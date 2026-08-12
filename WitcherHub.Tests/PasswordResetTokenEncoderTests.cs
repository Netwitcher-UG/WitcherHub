using WitcherHub.Infrastructure.Authentication;

namespace WitcherHub.Tests;

public class PasswordResetTokenEncoderTests
{
    [Theory]
    [InlineData("CfDJ8Abc+123/xyz=")]                 // the +, / and = that break query strings
    [InlineData("simple-token")]
    [InlineData("with spaces and ünïcode")]
    [InlineData("a")]
    public void EncodedTokensRoundTrip(string token)
    {
        var encoded = PasswordResetTokenEncoder.Encode(token);

        Assert.True(PasswordResetTokenEncoder.TryDecode(encoded, out var decoded));
        Assert.Equal(token, decoded);
    }

    [Fact]
    public void EncodedTokenIsSafeToPutInAQueryString()
    {
        var encoded = PasswordResetTokenEncoder.Encode("CfDJ8Abc+123/xyz==");

        Assert.DoesNotContain('+', encoded);
        Assert.DoesNotContain('/', encoded);
        Assert.DoesNotContain('=', encoded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not valid base64url !!")]
    public void MangledTokensAreRejectedRatherThanThrowing(string? encoded)
    {
        Assert.False(PasswordResetTokenEncoder.TryDecode(encoded, out var decoded));
        Assert.Equal("", decoded);
    }
}
