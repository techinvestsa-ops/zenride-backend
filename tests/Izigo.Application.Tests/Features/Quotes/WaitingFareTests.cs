using FluentAssertions;
using Izigo.Application.Features.Quotes.Helpers;
using Xunit;

namespace Izigo.Application.Tests.Features.Quotes;

public class WaitingFareTests
{
    private static readonly DateTime Arrived = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(9, 10, 1315, 0)]
    [InlineData(10, 10, 1315, 0)]
    [InlineData(11, 10, 1315, 1315)]
    [InlineData(12, 10, 1315, 2630)]
    [InlineData(0, 0, 200, 0)]
    [InlineData(3, 0, 200, 600)]
    public void Accrue_ChargesOnlyWholeMinutesAfterGrace(int elapsedMin, int graceMin, long perMin, long expected)
    {
        var (_, billable, fee) = WaitingFare.Accrue(Arrived, Arrived.AddMinutes(elapsedMin), graceMin, perMin);

        billable.Should().Be(Math.Max(0, elapsedMin - graceMin));
        fee.Should().Be(expected);
    }

    [Fact]
    public void Accrue_PartialMinuteInsideGrace_StaysFree()
    {
        var (_, billable, fee) = WaitingFare.Accrue(Arrived, Arrived.AddMinutes(10).AddSeconds(59), 10, 1315);

        billable.Should().Be(0);
        fee.Should().Be(0);
    }
}
