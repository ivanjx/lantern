using Lantern.Devices;

namespace Lantern.Tests;

public sealed class MacAddressTests
{
    [Theory]
    [InlineData("aa:bb:cc:dd:ee:ff")]
    [InlineData("AA-BB-CC-DD-EE-FF")]
    [InlineData("aabbccddeeff")]
    public void TryNormalize_AcceptsCommonFormats(string value)
    {
        var success = MacAddress.TryNormalize(value, out var normalized);

        Assert.True(success);
        Assert.Equal("AA:BB:CC:DD:EE:FF", normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("AA:BB:CC:DD:EE")]
    [InlineData("GG:BB:CC:DD:EE:FF")]
    [InlineData("AA:BB:CC:DD:EE:FF:00")]
    public void TryNormalize_RejectsInvalidValues(string value)
    {
        Assert.False(MacAddress.TryNormalize(value, out _));
    }
}
