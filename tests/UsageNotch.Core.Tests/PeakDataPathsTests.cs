using UsageNotch.Core;

namespace UsageNotch.Core.Tests;

public class PeakDataPathsTests
{
    [Fact]
    public void StoreFilesAreOutsideVirtualizedAppData()
    {
        var root = PeakDataPaths.ResolveRoot(@"C:\Users\Example", @"C:\Users\Example\AppData\Local", true);
        Assert.Equal(@"C:\Users\Example\.peak\store", root);
        Assert.DoesNotContain("AppData", root);
    }

    [Fact]
    public void DesktopFilesKeepTheirExistingLocation()
    {
        Assert.Equal(@"C:\Users\Example\AppData\Local\UsageNotch",
            PeakDataPaths.ResolveRoot(@"C:\Users\Example", @"C:\Users\Example\AppData\Local", false));
    }
}
