using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Safety.Helpers;

/// <summary>
/// Reports, the four-report auto-suspend, and the locked-account appeal thread.
/// A person is suspended once they have been reported more than three times.
/// </summary>
public static class AccountModeration
{
    public const int AutoSuspendAfter = 3;
    public const string SupportInbox = "support@izigo.app";

    private static readonly JobState[] ActiveJobStates =
    [
        JobState.Broadcasting, JobState.Offered, JobState.Accepted,
        JobState.EnRouteToPickup, JobState.ArrivedAtPickup,
        JobState.PickedUp, JobState.EnRouteToDropoff, JobState.ArrivedAtDropoff
    ];

    public static async Task FileReportAsync(
        IApplicationDbContext db,
        IRealtimeService realtime,
        IEmailService email,
        IPushService push,
        string reporterId,
        string? tripId,
        string? bookingId,
        string category,
        string description,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("VALIDATION_ERROR: Category and description are required.");

        var reporter = await db.Users.FirstOrDefaultAsync(u => u.Id == reporterId, ct)
            ?? throw new KeyNotFoundException("User not found.");

        var (reportedId, reportedRole) = await ResolveTargetAsync(db, reporter, tripId, bookingId, ct);
        if (reportedId == reporter.Id)
            throw new InvalidOperationException("CONFLICT: You cannot report yourself.");

        var duplicate = await db.AccountReports.AnyAsync(r =>
            r.ReporterUserId == reporter.Id &&
            r.ReportedUserId == reportedId &&
            r.TripId == tripId &&
            r.BookingId == bookingId, ct);
        if (duplicate)
            throw new InvalidOperationException("CONFLICT: You already reported this trip.");

        var reported = await db.Users.FirstOrDefaultAsync(u => u.Id == reportedId, ct)
            ?? throw new KeyNotFoundException("Reported account not found.");

        var prior = await db.AccountReports.CountAsync(r => r.ReportedUserId == reported.Id, ct);
        var count = prior + 1;

        var ticket = OpenTicket(
            reported.Id,
            reported.Role.ToString().ToLower(),
            "report",
            TicketPriority.High,
            $"Report against {reported.FullName} ({reportedRole}). " +
            $"From {reporter.FullName} ({reporter.Role.ToString().ToLower()}). " +
            $"Category: {category}. {description} This is report {count}.");
        if (tripId != null) ticket.TripId = tripId;
        db.SupportTickets.Add(ticket);
        db.SupportTicketMessages.Add(new SupportTicketMessage
        {
            TicketId = ticket.Id,
            SenderId = reporter.Id,
            SenderType = "user",
            Body = description.Trim()
        });
        db.AccountReports.Add(new AccountReport
        {
            ReporterUserId = reporter.Id,
            ReporterRole = reporter.Role.ToString().ToLower(),
            ReportedUserId = reported.Id,
            ReportedRole = reportedRole,
            TripId = tripId,
            BookingId = bookingId,
            Category = category.Trim(),
            Description = description.Trim(),
            TicketId = ticket.Id,
            Market = "ci"
        });

        var autoSuspended = false;
        if (count > AutoSuspendAfter && reported.Status == UserStatus.Active)
        {
            await LockAccountAsync(
                db, reported,
                $"Automatically suspended after {count} reports.", ct);
            autoSuspended = true;
        }

        await db.SaveChangesAsync(ct);

        await PublishAdminAsync(realtime, "ticket.created", new
        {
            ticket_id = ticket.Id,
            category = "report",
            reported_user_id = reported.Id,
            report_count = count,
            auto_suspended = autoSuspended
        }, ct);

        if (autoSuspended)
            await AnnounceAsync(db, realtime, email, push, reported, locked: true, ct);
    }

    public static async Task LockAccountAsync(
        IApplicationDbContext db, User user, string reason, CancellationToken ct)
    {
        user.Status = UserStatus.Suspended;
        user.SuspensionReason = reason;

        var profile = await db.DriverProfiles
            .FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
        if (profile != null)
        {
            profile.IsOnline = false;
            var trips = await db.Trips
                .Where(t => (t.DriverId == user.Id || t.DriverId == profile.Id) &&
                            ActiveJobStates.Contains(t.JobState))
                .ToListAsync(ct);
            foreach (var trip in trips)
            {
                trip.JobState = JobState.CancelledByDriver;
                trip.CancelledAt = DateTime.UtcNow;
            }

            var listings = await db.CoRideListings
                .Where(l => l.DriverId == user.Id && l.Status == "open")
                .ToListAsync(ct);
            foreach (var listing in listings)
                listing.Status = "suspended";
        }

        await EnsureAppealAsync(db, user, reason, ct);
    }

    public static async Task ReleaseListingsAsync(
        IApplicationDbContext db, User user, CancellationToken ct)
    {
        var listings = await db.CoRideListings
            .Where(l => l.DriverId == user.Id &&
                        l.Status == "suspended" &&
                        l.DepartureAt > DateTime.UtcNow)
            .ToListAsync(ct);
        foreach (var listing in listings)
            listing.Status = "open";
    }

    public static async Task AnnounceAsync(
        IApplicationDbContext db,
        IRealtimeService realtime,
        IEmailService email,
        IPushService push,
        User user,
        bool locked,
        CancellationToken ct)
    {
        var reason = user.SuspensionReason ?? "Your account is under review.";
        var eventName = !locked
            ? "account.reinstated"
            : user.Status == UserStatus.Blocked ? "account.blocked" : "account.suspended";
        var payload = new { reason, appeal_only = locked };

        try
        {
            if (user.Role == UserRole.Driver)
                await realtime.PublishToDriverAsync(user.Id, eventName, payload, ct);
            else
                await realtime.PublishToUserAsync(user.Id, eventName, payload, ct);
        }
        catch { /* a missed socket event must not undo the status change */ }

        if (!locked)
        {
            await EmailAsync(email, user.Email, user.FullName,
                "Your ZenRide account is active again",
                $"<p>Hello {user.FullName},</p><p>Your account has been restored. You can use the app again.</p>",
                ct);
            return;
        }

        var blocked = user.Status == UserStatus.Blocked;
        var subject = blocked ? "Your ZenRide account is blocked" : "Your ZenRide account is suspended";
        var body = blocked
            ? $"<p>Hello {user.FullName},</p><p>{reason}</p><p>This account cannot sign in. Email {SupportInbox} if you need help.</p>"
            : $"<p>Hello {user.FullName},</p><p>{reason}</p><p>The app is locked except for an appeal. Open ZenRide, write to support, and we will reply in the app and by email.</p>";
        await EmailAsync(email, user.Email, user.FullName, subject, body, ct);
        await EmailAsync(email, SupportInbox, "ZenRide support",
            $"Account suspended: {user.FullName}",
            $"<p>{user.FullName} ({user.Role}, {user.Phone}) was suspended.</p><p>{reason}</p>",
            ct);

        try
        {
            var tokens = await db.UserDevices
                .Where(d => d.UserId == user.Id && d.FcmToken != null && d.FcmToken != "")
                .Select(d => d.FcmToken!)
                .Distinct()
                .ToListAsync(ct);
            if (tokens.Count > 0)
                await push.SendBatchAsync(tokens, "Account suspended", reason, "account.suspended", ct: ct);
        }
        catch { /* push is best-effort */ }
    }

    public static async Task NotifyStaffReplyAsync(
        IApplicationDbContext db,
        IRealtimeService realtime,
        IEmailService email,
        IPushService push,
        SupportTicket ticket,
        string body,
        CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == ticket.UserId, ct);
        if (user is null) return;

        var payload = new { ticket_id = ticket.Id, body, from = "staff" };
        try
        {
            if (user.Role == UserRole.Driver)
                await realtime.PublishToDriverAsync(user.Id, "account.message", payload, ct);
            else
                await realtime.PublishToUserAsync(user.Id, "account.message", payload, ct);
        }
        catch { }

        await EmailAsync(email, user.Email, user.FullName,
            "ZenRide support replied",
            $"<p>Hello {user.FullName},</p><p>{body}</p><p>Open the app to reply.</p>",
            ct);

        try
        {
            var tokens = await db.UserDevices
                .Where(d => d.UserId == user.Id && d.FcmToken != null && d.FcmToken != "")
                .Select(d => d.FcmToken!)
                .Distinct()
                .ToListAsync(ct);
            if (tokens.Count > 0)
                await push.SendBatchAsync(tokens, "Support replied", body, "account.message", ct: ct);
        }
        catch { }
    }

    public static async Task NotifyUserReplyAsync(
        IRealtimeService realtime,
        IEmailService email,
        SupportTicket ticket,
        User user,
        string body,
        CancellationToken ct)
    {
        await PublishAdminAsync(realtime, "ticket.replied", new
        {
            ticket_id = ticket.Id,
            category = ticket.Category,
            user_id = user.Id
        }, ct);

        await EmailAsync(email, SupportInbox, "ZenRide support",
            $"Appeal from {user.FullName}",
            $"<p>{user.FullName} ({user.Phone}) wrote:</p><p>{body}</p>",
            ct);
    }

    private static async Task<(string UserId, string Role)> ResolveTargetAsync(
        IApplicationDbContext db, User reporter, string? tripId, string? bookingId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(tripId))
        {
            var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == tripId, ct)
                ?? throw new KeyNotFoundException("Trip not found.");

            if (reporter.Id == trip.RiderId)
            {
                if (string.IsNullOrWhiteSpace(trip.DriverId))
                    throw new InvalidOperationException("CONFLICT: This trip has no driver to report yet.");
                var driverUserId = await DriverUserIdAsync(db, trip.DriverId, ct);
                return (driverUserId, "driver");
            }

            var driverId = await DriverUserIdAsync(db, trip.DriverId ?? "", ct);
            if (reporter.Id == driverId || reporter.Id == trip.DriverId)
                return (trip.RiderId, "rider");

            throw new InvalidOperationException("CONFLICT: You are not part of this trip.");
        }

        if (!string.IsNullOrWhiteSpace(bookingId))
        {
            var booking = await db.CoRideBookings
                .Include(b => b.Listing)
                .FirstOrDefaultAsync(b => b.Id == bookingId, ct)
                ?? throw new KeyNotFoundException("Booking not found.");

            if (reporter.Id == booking.RiderId)
                return (booking.Listing.DriverId, "driver");
            if (reporter.Id == booking.Listing.DriverId)
                return (booking.RiderId, "rider");

            throw new InvalidOperationException("CONFLICT: You are not part of this co-ride.");
        }

        throw new ArgumentException("VALIDATION_ERROR: A trip or co-ride booking is required.");
    }

    private static async Task<string> DriverUserIdAsync(
        IApplicationDbContext db, string driverId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(driverId))
            throw new InvalidOperationException("CONFLICT: This trip has no driver to report yet.");

        var profile = await db.DriverProfiles
            .Where(p => p.Id == driverId || p.UserId == driverId)
            .Select(p => p.UserId)
            .FirstOrDefaultAsync(ct);
        return profile ?? driverId;
    }

    private static async Task EnsureAppealAsync(
        IApplicationDbContext db, User user, string reason, CancellationToken ct)
    {
        var open = await db.SupportTickets.AnyAsync(t =>
            t.UserId == user.Id &&
            t.Category == "appeal" &&
            t.Status != TicketStatus.Closed &&
            t.Status != TicketStatus.Resolved, ct);
        if (open) return;

        var ticket = OpenTicket(
            user.Id,
            user.Role.ToString().ToLower(),
            "appeal",
            TicketPriority.Urgent,
            reason);
        db.SupportTickets.Add(ticket);
        db.SupportTicketMessages.Add(new SupportTicketMessage
        {
            TicketId = ticket.Id,
            SenderId = "system",
            SenderType = "staff",
            Body = "Your account is suspended. Reply here to appeal. Support will answer in the app and by email."
        });
    }

    private static SupportTicket OpenTicket(
        string userId, string role, string category, TicketPriority priority, string description) =>
        new()
        {
            UserId = userId,
            UserRole = role,
            Category = category,
            Description = description,
            Status = TicketStatus.Open,
            Priority = priority,
            Reference = $"TKT-{Guid.CreateVersion7():N}"[..12].ToUpper(),
            Market = "ci",
            SlaDeadline = DateTime.UtcNow.AddMinutes(15)
        };

    private static Task PublishAdminAsync(
        IRealtimeService realtime, string eventName, object payload, CancellationToken ct)
    {
        try { return realtime.PublishToAdminAsync("ci", eventName, payload, ct); }
        catch { return Task.CompletedTask; }
    }

    private static async Task EmailAsync(
        IEmailService email, string? to, string name, string subject, string html, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(to)) return;
        try { await email.SendAsync(to, string.IsNullOrWhiteSpace(name) ? to : name, subject, html, ct); }
        catch { /* email is best-effort; the in-app thread is the source of truth */ }
    }
}
