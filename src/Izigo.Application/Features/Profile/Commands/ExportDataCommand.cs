using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Profile.Dtos;
using Izigo.Domain.Entities;
using MediatR;

namespace Izigo.Application.Features.Profile.Commands;

public record ExportDataCommand(string UserId) : IRequest<ExportDataResult>;

public class ExportDataHandler(IApplicationDbContext db) : IRequestHandler<ExportDataCommand, ExportDataResult>
{
    public async Task<ExportDataResult> Handle(ExportDataCommand req, CancellationToken ct)
    {
        _ = await db.Users.FindAsync([req.UserId], ct)
            ?? throw new KeyNotFoundException("User not found.");

        // Queue a background job — Hangfire job enqueued in the API layer
        // The job reads the user's data, builds a ZIP, uploads it, and emails the download link.
        // Here we just record the intent.
        db.BackgroundJobs.Add(new BackgroundJob
        {
            Type = "data_export",
            Status = "queued",
            InitiatedByStaffId = null,    // user-initiated
        });

        await db.SaveChangesAsync(ct);

        return new ExportDataResult(
            "Your data export has been queued. You will receive an email with a download link within 24 hours."
        );
    }
}
