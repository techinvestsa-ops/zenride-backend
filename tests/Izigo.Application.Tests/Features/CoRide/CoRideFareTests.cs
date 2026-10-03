using FluentAssertions;
using Izigo.Application.Features.CoRide.Helpers;
using Xunit;

namespace Izigo.Application.Tests.Features.CoRide;

public class CoRideFareTests
{
    [Fact]
    public void FourSeats_SplitTheTripFareEvenly()
    {
        CoRideFare.Share(32_000, 4).Should().Be(8_000);
    }

    [Fact]
    public void ThreeSeats_RoundUpSoTheTripIsCovered()
    {
        CoRideFare.Share(10_000, 3).Should().Be(3_334);
    }
}
