using FluentAssertions;
using Izigo.Application.Features.Dispatch;
using Xunit;

namespace Izigo.Application.Tests.Features.Dispatch;

public class DispatchMatchingTests
{
    [Fact]
    public void Disk_RingOne_HasTheHomeCellAndSixNeighbours()
    {
        var home = HexGrid.FromLatLng(5.36, -4.008);
        HexGrid.Disk(home, 0).Should().Equal(home);
        HexGrid.Disk(home, 1).Should().HaveCount(7);
        HexGrid.Disk(home, 2).Should().HaveCount(19);
    }

    [Fact]
    public void NearbyDriver_StaysInTheFirstRing_AFarOneDoesNot()
    {
        const double lat = 5.36;
        const double lng = -4.008;
        var home = HexGrid.FromLatLng(lat, lng);
        var close = HexGrid.FromLatLng(lat + 400.0 / 110_540.0, lng);
        var far = HexGrid.FromLatLng(lat + 2500.0 / 110_540.0, lng);

        HexGrid.Disk(home, 1).Should().Contain(close);
        HexGrid.Disk(home, 1).Should().NotContain(far);
        HexGrid.RingsForRadius(3000).Should().BeGreaterThan(HexGrid.RingsForRadius(1000));
    }

    [Fact]
    public void Batch_GivesTheSharedDriverToTheRiderWhoWouldOtherwiseWaitLongest()
    {
        // You: driver A is 4 min, driver B is 5 min.
        // John: driver A is 2 min, and his only other option is 15 min.
        // The batch gives A to John and B to you. Total wait is 7 min,
        // against 19 min if you had taken A.
        var cost = new long[,]
        {
            { 4 * 60, 5 * 60 },
            { 2 * 60, 15 * 60 }
        };

        var assigned = MinCostMatcher.Solve(cost);

        assigned[0].Should().Be(1);
        assigned[1].Should().Be(0);
    }

    [Fact]
    public void Batch_LeavesARiderUnmatched_WhenEveryDriverIsForbidden()
    {
        var cost = new long[,]
        {
            { 60, MinCostMatcher.Forbidden },
            { MinCostMatcher.Forbidden, MinCostMatcher.Forbidden }
        };

        var assigned = MinCostMatcher.Solve(cost);

        assigned[0].Should().Be(0);
        assigned[1].Should().Be(-1);
    }
}
