using FluentAssertions;
using Izigo.Application.Common.Helpers;
using Izigo.Application.Features.Driver.Commands;
using Izigo.Application.Features.Quotes.Commands;
using Izigo.Application.Features.Quotes.Dtos;
using Izigo.Application.Features.Rides.Helpers;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Xunit;

namespace Izigo.Application.Tests.Features.Rides;

/// <summary>Stops on the way: priced into the quote, carried by the trip, ticked off by the driver.</summary>
public class TripStopTests
{
    private static CreateQuoteRequest Ride(params QuoteLocationInput[] stops) => new(
        "ride",
        new QuoteLocationInput("Plateau", 5.3236, -4.0205, null),
        new QuoteLocationInput("Yopougon", 5.3450, -4.0900, null),
        null, null, null, null, stops);

    [Fact]
    public async Task A_quote_keeps_its_stops_in_order()
    {
        using var db = DbContextFactory.Create();
        var quote = await new CreateQuoteHandler(db, FakeServices.Geo(), FakeServices.QuoteSettings())
            .Handle(new CreateQuoteCommand("usr_q", Ride(
                new QuoteLocationInput("Pharmacy", 5.33, -4.05, null),
                new QuoteLocationInput("School", 5.34, -4.07, null))), default);

        quote.Stops!.Select(s => s.Label).Should().Equal("Pharmacy", "School");
        TripStops.Read(db.Quotes.Single().StopsJson).Should().HaveCount(2);
    }

    [Fact]
    public async Task More_than_two_stops_or_stops_on_a_package_are_refused()
    {
        using var db = DbContextFactory.Create();
        var handler = new CreateQuoteHandler(db, FakeServices.Geo(), FakeServices.QuoteSettings());
        var s = new QuoteLocationInput("Stop", 5.33, -4.05, null);

        var three = () => handler.Handle(new CreateQuoteCommand("usr_q", Ride(s, s, s)), default);
        var package = () => handler.Handle(new CreateQuoteCommand("usr_q", Ride(s) with { Vertical = "package" }), default);

        await three.Should().ThrowAsync<ArgumentException>();
        await package.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task The_driver_ticks_off_a_stop_once_and_the_rider_sees_it()
    {
        using var db = DbContextFactory.Create();
        var trip = new Trip
        {
            RiderId = "usr_r", DriverId = "usr_d", JobState = JobState.EnRouteToDropoff, Vertical = Vertical.Ride,
            Market = "ci", Currency = "XOF", ServiceClass = ServiceClass.ZenCar,
            StopsJson = TripStops.Write([new TripStop("Pharmacy", 5.33, -4.05), new TripStop("School", 5.34, -4.07)]),
        };
        db.Trips.Add(trip);
        await db.SaveChangesAsync();
        var handler = new ArrivedAtStopHandler(db, FakeServices.Realtime());

        var job = await handler.Handle(new ArrivedAtStopCommand("usr_d", trip.Id, 0), default);
        var firstTime = TripStops.Read(db.Trips.Single().StopsJson)[0].ArrivedAt;
        await handler.Handle(new ArrivedAtStopCommand("usr_d", trip.Id, 0), default);

        job.Stops!.Select(s => s.Reached).Should().Equal(true, false);
        TripStops.Read(db.Trips.Single().StopsJson)[0].ArrivedAt.Should().Be(firstTime);
        var ride = await RideDetailMapper.BuildAsync(db.Trips.Single(), db, default, "", 0);
        ride.Stops!.Select(s => s.Reached).Should().Equal(true, false);
    }
}
