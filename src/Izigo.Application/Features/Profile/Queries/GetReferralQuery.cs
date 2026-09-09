using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Settings;
using Izigo.Application.Features.Profile.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Izigo.Application.Features.Profile.Queries;

public record GetReferralQuery(string UserId) : IRequest<ReferralDto>;

public class GetReferralHandler(
    IApplicationDbContext db,
    IOptions<AppSettings> appOptions)
    : IRequestHandler<GetReferralQuery, ReferralDto>
{
    public async Task<ReferralDto> Handle(GetReferralQuery req, CancellationToken ct)
    {
        var cfg      = appOptions.Value;
        var termsUrl = $"{cfg.ApiBaseUrl.TrimEnd('/')}/pages/referral-terms";

        var user = await db.Users
            .Where(u => u.Id == req.UserId)
            .Select(u => new { u.ReferralCode, u.Language })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("User not found.");

        var code = user.ReferralCode ?? "";

        var invitedCount = await db.Users
            .CountAsync(u => u.ReferredByUserId == req.UserId, ct);

        var qualifiedReferrals = await db.Users
            .Where(u => u.ReferredByUserId == req.UserId)
            .Join(db.Trips, u => u.Id, t => t.RiderId,
                (u, t) => new { u.Id, t.JobState })
            .Where(x => x.JobState == Domain.Enums.JobState.Completed)
            .Select(x => x.Id)
            .Distinct()
            .CountAsync(ct);

        var earnedTotal = qualifiedReferrals * cfg.ReferralRewardPerInvite;

        var shareMessage = user.Language == "fr"
            ? $"Utilisez mon code {code} sur Izigo pour obtenir une réduction sur votre premier trajet !"
            : $"Use my code {code} on Izigo to get a discount on your first trip!";

        return new ReferralDto(
            Code:            code,
            ShareMessage:    shareMessage,
            InvitedCount:    invitedCount,
            EarnedTotal:     earnedTotal,
            RewardPerInvite: cfg.ReferralRewardPerInvite,
            TermsUrl:        termsUrl
        );
    }
}
