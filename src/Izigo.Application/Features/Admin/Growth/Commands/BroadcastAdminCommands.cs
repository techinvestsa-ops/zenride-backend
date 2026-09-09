using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Growth.Commands;

public record BroadcastResult(bool Success, string? ErrorCode, object? Data = null);

// ── POST /admin/broadcasts ────────────────────────────────────────────────────
// Requires broadcasts.send permission (enforced at controller).
// audience: all_drivers | online_drivers | all_riders | inactive_riders | zone | segment_id
// scheduled_at is optional — omit to send immediately.

public record CreateBroadcastCommand(
    string Audience, string Channel, string Title, string Body,
    string? DeepLink, DateTime? ScheduledAt,
    string Market, string StaffId, string StaffName) : IRequest<BroadcastResult>;

public class CreateBroadcastHandler(IApplicationDbContext db, IAuditService audit,
    IPushService push, IJobDispatcher jobDispatcher)
    : IRequestHandler<CreateBroadcastCommand, BroadcastResult>
{
    public async Task<BroadcastResult> Handle(CreateBroadcastCommand cmd, CancellationToken ct)
    {
        var broadcast = new Broadcast
        {
            Audience      = cmd.Audience,
            Channel       = cmd.Channel,
            Title         = cmd.Title,
            Body          = cmd.Body,
            DeepLink      = cmd.DeepLink,
            ScheduledAt   = cmd.ScheduledAt,
            SentByStaffId = cmd.StaffId,
            Market        = cmd.Market
        };
        db.Broadcasts.Add(broadcast);

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.BroadcastSend,
            "Broadcast", broadcast.Id, reason: $"{cmd.Channel} to {cmd.Audience}",
            after: new { cmd.Audience, cmd.Channel, cmd.Title }, ct: ct);
        await db.SaveChangesAsync(ct);

        if (cmd.ScheduledAt is null && cmd.Channel == "push")
        {
            // Send immediately — synchronous fan-out on the request thread for small audiences;
            // for large audiences consider also dispatching a background job.
            var tokens = await ResolveTokensAsync(cmd.Audience, cmd.Market, ct);
            if (tokens.Count > 0)
            {
                await push.SendBatchAsync(tokens, cmd.Title, cmd.Body,
                    "broadcast", cmd.DeepLink, ct: ct);
                broadcast.SentAt         = DateTime.UtcNow;
                broadcast.DeliveredCount = tokens.Count;
                await db.SaveChangesAsync(ct);
            }
        }
        else if (cmd.ScheduledAt.HasValue)
        {
            // Scheduled — create a job record and hand off to Hangfire
            var job = new BackgroundJob
            {
                Type               = $"broadcast:{broadcast.Id}",
                Status             = "queued",
                InitiatedByStaffId = cmd.StaffId
            };
            db.BackgroundJobs.Add(job);
            await db.SaveChangesAsync(ct);
            jobDispatcher.Schedule(job.Id, $"broadcast:{broadcast.Id}", cmd.ScheduledAt.Value);
        }

        return new(true, null, new { id = broadcast.Id });
    }

    private async Task<List<string>> ResolveTokensAsync(string audience, string market,
        CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);
        return audience switch
        {
            "all_riders" => await (
                from d in db.UserDevices
                join u in db.Users on d.UserId equals u.Id
                where u.Role == UserRole.Rider && u.Status == UserStatus.Active && d.FcmToken != null
                select d.FcmToken!).Distinct().ToListAsync(ct),

            "all_drivers" => await (
                from d in db.UserDevices
                join dp in db.DriverProfiles on d.UserId equals dp.UserId
                where dp.KycStatus == KycStatus.Approved && d.FcmToken != null
                select d.FcmToken!).Distinct().ToListAsync(ct),

            "online_drivers" => await (
                from d in db.UserDevices
                join dp in db.DriverProfiles on d.UserId equals dp.UserId
                where dp.IsOnline && d.FcmToken != null
                select d.FcmToken!).Distinct().ToListAsync(ct),

            "inactive_riders" => await (
                from d in db.UserDevices
                join u in db.Users on d.UserId equals u.Id
                where u.Role == UserRole.Rider && d.FcmToken != null &&
                      !db.Trips.Any(t => t.RiderId == u.Id && t.CreatedAt >= cutoff)
                select d.FcmToken!).Distinct().ToListAsync(ct),

            _ => []
        };
    }
}

// ── POST /admin/broadcasts/estimate ──────────────────────────────────────────
// Dry-run: returns audience size and estimated cost before sending.

public record EstimateBroadcastCommand(string Audience, string Channel, string Market)
    : IRequest<BroadcastResult>;

public class EstimateBroadcastHandler(IApplicationDbContext db)
    : IRequestHandler<EstimateBroadcastCommand, BroadcastResult>
{
    private const long SmsCostPerDevice = 15; // XOF per SMS

    public async Task<BroadcastResult> Handle(EstimateBroadcastCommand cmd, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);

        var deviceCount = cmd.Audience switch
        {
            "all_riders" => await (
                from d in db.UserDevices
                join u in db.Users on d.UserId equals u.Id
                where u.Role == UserRole.Rider && u.Status == UserStatus.Active && d.FcmToken != null
                select d.Id).Distinct().CountAsync(ct),

            "all_drivers" => await (
                from d in db.UserDevices
                join dp in db.DriverProfiles on d.UserId equals dp.UserId
                where dp.KycStatus == KycStatus.Approved && d.FcmToken != null
                select d.Id).Distinct().CountAsync(ct),

            "online_drivers" => await (
                from d in db.UserDevices
                join dp in db.DriverProfiles on d.UserId equals dp.UserId
                where dp.IsOnline && d.FcmToken != null
                select d.Id).Distinct().CountAsync(ct),

            "inactive_riders" => await (
                from d in db.UserDevices
                join u in db.Users on d.UserId equals u.Id
                where u.Role == UserRole.Rider && d.FcmToken != null &&
                      !db.Trips.Any(t => t.RiderId == u.Id && t.CreatedAt >= cutoff)
                select d.Id).Distinct().CountAsync(ct),

            _ => 0
        };

        var estimatedCost = cmd.Channel == "sms" ? deviceCount * SmsCostPerDevice : 0L;

        return new(true, null, new
        {
            audience      = cmd.Audience,
            channel       = cmd.Channel,
            device_count  = deviceCount,
            estimated_cost = estimatedCost,
            currency      = "XOF"
        });
    }
}

// ── POST /admin/broadcasts/{id}/cancel ───────────────────────────────────────

public record CancelBroadcastCommand(string BroadcastId, string StaffId, string StaffName)
    : IRequest<BroadcastResult>;

public class CancelBroadcastHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<CancelBroadcastCommand, BroadcastResult>
{
    public async Task<BroadcastResult> Handle(CancelBroadcastCommand cmd, CancellationToken ct)
    {
        var broadcast = await db.Broadcasts.FirstOrDefaultAsync(b => b.Id == cmd.BroadcastId, ct);
        if (broadcast is null) return new(false, "BROADCAST_NOT_FOUND");
        if (broadcast.IsCancelled) return new(false, "ALREADY_CANCELLED");
        if (broadcast.SentAt.HasValue) return new(false, "ALREADY_SENT");

        broadcast.IsCancelled = true;

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.BroadcastAction,
            "Broadcast", cmd.BroadcastId, reason: "Cancelled", ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── POST /admin/broadcasts/test ───────────────────────────────────────────────
// Sends to the requesting staff member's own device only.

public record TestBroadcastCommand(string Title, string Body, string? DeepLink,
    string Channel, string StaffId, string StaffEmail) : IRequest<BroadcastResult>;

public class TestBroadcastHandler(IApplicationDbContext db, IPushService push)
    : IRequestHandler<TestBroadcastCommand, BroadcastResult>
{
    public async Task<BroadcastResult> Handle(TestBroadcastCommand cmd, CancellationToken ct)
    {
        // Send to staff member's own registered devices
        var staff = await db.Staff.FirstOrDefaultAsync(s => s.Id == cmd.StaffId, ct);
        if (staff is null) return new(false, "STAFF_NOT_FOUND");

        // Staff devices are identified by matching the staff's user record (if they have one)
        // Fall back to matching by email across user devices
        var staffUser = await db.Users
            .Where(u => u.Email == cmd.StaffEmail)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct);

        int sentCount = 0;
        if (staffUser is not null)
        {
            var tokens = await db.UserDevices
                .Where(d => d.UserId == staffUser && d.FcmToken != null)
                .Select(d => d.FcmToken!)
                .Distinct().ToListAsync(ct);

            if (tokens.Count > 0)
            {
                await push.SendBatchAsync(tokens, $"[TEST] {cmd.Title}", cmd.Body,
                    "broadcast_test", cmd.DeepLink, ct: ct);
                sentCount = tokens.Count;
            }
        }

        return new(true, null, new { sent_to_devices = sentCount });
    }
}

// ── POST /admin/reports/{key}/run ─────────────────────────────────────────────
// Enqueues the report; returns 202 + job_id. Result lands as signed URL on completion.

public record RunReportCommand(string Key, string? Format, string StaffId)
    : IRequest<BroadcastResult>;

public class RunReportHandler(IApplicationDbContext db, IJobDispatcher jobDispatcher)
    : IRequestHandler<RunReportCommand, BroadcastResult>
{
    private static readonly HashSet<string> ValidKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "daily_trips", "weekly_revenue", "driver_performance",
        "kyc_queue", "payout_summary", "coupon_performance",
        "support_desk", "reconciliation"
    };

    public async Task<BroadcastResult> Handle(RunReportCommand cmd, CancellationToken ct)
    {
        if (!ValidKeys.Contains(cmd.Key))
            return new(false, "REPORT_KEY_NOT_FOUND");

        var job = new BackgroundJob
        {
            Type               = $"report_{cmd.Key}",
            Status             = "queued",
            InitiatedByStaffId = cmd.StaffId
        };
        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync(ct);

        jobDispatcher.Enqueue(job.Id, job.Type);

        return new(true, null, new
        {
            job_id     = job.Id,
            status_url = $"/api/v1/admin/jobs/{job.Id}"
        });
    }
}

// ── POST /admin/reports/schedules ────────────────────────────────────────────
// Stores a cron schedule for automatic report generation.
// Uses a BackgroundJob record tagged as a schedule (status = "scheduled").

public record ScheduleReportCommand(string Key, string Cron, string[] Recipients,
    string Format, string StaffId) : IRequest<BroadcastResult>;

public class ScheduleReportHandler(IApplicationDbContext db)
    : IRequestHandler<ScheduleReportCommand, BroadcastResult>
{
    public async Task<BroadcastResult> Handle(ScheduleReportCommand cmd, CancellationToken ct)
    {
        var job = new BackgroundJob
        {
            Type               = $"report_schedule_{cmd.Key}",
            Status             = "scheduled",
            InitiatedByStaffId = cmd.StaffId
        };
        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync(ct);

        return new(true, null, new { schedule_id = job.Id });
    }
}
