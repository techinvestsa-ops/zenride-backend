using FluentAssertions;
using Izigo.Application.Features.Driver.Queries;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Xunit;

namespace Izigo.Application.Tests.Features.Driver;

/// <summary>With a next trip queued, the trip under way stays the driver's active job.</summary>
public class NextTripQueueTests
{
    [Fact]
    public async Task The_trip_under_way_is_the_active_job_not_the_queued_one()
    {
        using var db = DbContextFactory.Create();
        Trip New(JobState s, string code) => new()
        {
            RiderId = "usr_r_" + code, DriverId = "usr_d", JobState = s, Code = code, Vertical = Vertical.Ride,
            Market = "ci", Currency = "XOF", ServiceClass = ServiceClass.ZenCar,
        };
        db.Trips.Add(New(JobState.Accepted, "NEXT"));
        db.Trips.Add(New(JobState.EnRouteToDropoff, "NOW"));
        await db.SaveChangesAsync();

        var active = await new GetActiveJobHandler(db).Handle(new GetActiveJobQuery("usr_d"), default);

        active!.Code.Should().Be("NOW");
    }
}
