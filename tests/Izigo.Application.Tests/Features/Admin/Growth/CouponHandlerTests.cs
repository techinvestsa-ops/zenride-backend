using FluentAssertions;
using Izigo.Application.Features.Admin.Growth.Commands;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Xunit;

namespace Izigo.Application.Tests.Features.Admin.Growth;

public class CouponHandlerTests
{
    private static Coupon BuildCoupon(string code, int redemptions = 0) => new()
    {
        Code            = code,
        Title           = "Test Coupon",
        DiscountType    = "percent",
        Value           = 10,
        MaxDiscount     = 500,
        MinOrder        = 1000,
        RedemptionCount = redemptions,
        Market          = "ci",
        IsActive        = true
    };

    [Fact]
    public async Task UpdateCoupon_CanChangeTitle_WhenRedemptionsExist()
    {
        using var db = DbContextFactory.Create();
        db.Coupons.Add(BuildCoupon("SAVE10", redemptions: 5));
        await db.SaveChangesAsync();

        var handler = new UpdateCouponHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(new UpdateCouponCommand(
            "SAVE10", "ci", Title: "Updated Title",
            DiscountType: null, Value: null, MaxDiscount: null, MinOrder: null,
            Verticals: null, Zones: null, FirstTripOnly: null,
            PerUserLimit: null, TotalCap: null, StartsAt: null, ExpiresAt: null, IsActive: null,
            StaffId: "stf_1", StaffName: "Admin"), CancellationToken.None);

        result.Success.Should().BeTrue();
        db.Coupons.First(c => c.Code == "SAVE10").Title.Should().Be("Updated Title");
    }

    [Fact]
    public async Task UpdateCoupon_CannotChangeDiscountType_WhenRedemptionsExist()
    {
        using var db = DbContextFactory.Create();
        db.Coupons.Add(BuildCoupon("SAVE20", redemptions: 3));
        await db.SaveChangesAsync();

        var handler = new UpdateCouponHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(new UpdateCouponCommand(
            "SAVE20", "ci", Title: null,
            DiscountType: "fixed", Value: null, MaxDiscount: null, MinOrder: null,
            Verticals: null, Zones: null, FirstTripOnly: null,
            PerUserLimit: null, TotalCap: null, StartsAt: null, ExpiresAt: null, IsActive: null,
            StaffId: "stf_1", StaffName: "Admin"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("CANNOT_CHANGE_DISCOUNT_AFTER_REDEMPTIONS");
    }

    [Fact]
    public async Task UpdateCoupon_CanChangeDiscountType_WhenNoRedemptions()
    {
        using var db = DbContextFactory.Create();
        db.Coupons.Add(BuildCoupon("NEW10", redemptions: 0));
        await db.SaveChangesAsync();

        var handler = new UpdateCouponHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(new UpdateCouponCommand(
            "NEW10", "ci", Title: null,
            DiscountType: "fixed", Value: 200L, MaxDiscount: null, MinOrder: null,
            Verticals: null, Zones: null, FirstTripOnly: null,
            PerUserLimit: null, TotalCap: null, StartsAt: null, ExpiresAt: null, IsActive: null,
            StaffId: "stf_1", StaffName: "Admin"), CancellationToken.None);

        result.Success.Should().BeTrue();
        var coupon = db.Coupons.First(c => c.Code == "NEW10");
        coupon.DiscountType.Should().Be("fixed");
        coupon.Value.Should().Be(200);
    }

    [Fact]
    public async Task CreateCoupon_DuplicateCode_ReturnsError()
    {
        using var db = DbContextFactory.Create();
        db.Coupons.Add(BuildCoupon("DUPE"));
        await db.SaveChangesAsync();

        var handler = new CreateCouponHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(new CreateCouponCommand(
            "DUPE", "Title", "percent", 10, 500, 1000,
            [], [], false, 1, 100, null, null,
            "ci", "stf_1", "Admin"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("COUPON_CODE_TAKEN");
    }
}
