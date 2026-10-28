using Stride.Water;
using Xunit;

namespace Stride.Water.Tests;

public class SeaShoreMapTests
{
    // A beach along x = 0: land (above the level, 0) to the west, the floor falling 1 m a metre to the east.
    private static SeaShoreMap Beach() => SeaShoreMap.Build(20, -10f, -10f, 20f, 0f, (x, z) => -x);

    [Fact]
    public void LandIsAtNoDistanceAndWaterIsAsFarAsTheShore()
    {
        var map = Beach();
        // Place 2 is at x = -7.5 (land); place 15 at x = 5.5, 5.5 m out from the last land place (at x = -0.5) less half a cell.
        Assert.Equal(0f, map.Distance[10 * 20 + 2]);
        Assert.InRange(map.Distance[10 * 20 + 15], 5.5f, 6.5f);
        Assert.Equal(5.5f, map.Depth[10 * 20 + 15], 3);
    }

    [Fact]
    public void TheShoreIsTowardsTheLand()
    {
        var map = Beach();
        var i = 10 * 20 + 15;
        Assert.Equal(-1f, map.DirectionX[i], 3);
        Assert.Equal(0f, map.DirectionZ[i], 3);
    }

    [Fact]
    public void NoFloorIsDeepWater()
    {
        var map = SeaShoreMap.Build(8, 0f, 0f, 8f, 0f, (x, z) => float.NaN);
        Assert.All(map.Depth, d => Assert.Equal(1000f, d));
    }
}
