using UsageNotch.Core;

namespace UsageNotch.Core.Tests;

public sealed class FreewareTests
{
    [Theory]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--settings" }, false)]
    [InlineData(new[] { "--inspect" }, false)]
    [InlineData(new[] { "--demo-cycle" }, false)]
    [InlineData(new[] { "--demo" }, true)]
    public void OnlyExplicitDemoUsesSampleData(string[] arguments, bool expected) =>
        Assert.Equal(expected, SessionMode.IsDemo(arguments));

    [Fact] public void MissingPublishingDetailsDoNotPutAppIntoDemo()
    {
        Assert.False(new PublishingConfiguration().IsComplete);
        Assert.False(SessionMode.IsDemo([]));
    }

    [Theory]
    [InlineData("https://peakforwindows.vercel.app", true)]
    [InlineData("https://user:password@example.com", false)]
    [InlineData("http://example.com", false)]
    [InlineData("file:///C:/private", false)]
    public void ContactLinksRequireHttps(string url, bool expected) =>
        Assert.Equal(expected, PublishingConfiguration.IsHttpsUrl(url));

    [Theory]
    [InlineData("peakforwindows.support@gmail.com", true)]
    [InlineData("Name <test@example.com>", false)]
    [InlineData("test@example.com\r\nBcc: other@example.com", false)]
    public void SupportEmailCannotInjectHeaders(string value, bool expected) =>
        Assert.Equal(expected, PublishingConfiguration.IsSupportEmail(value));
}
