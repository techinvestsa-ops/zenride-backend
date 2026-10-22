using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Driver.Dtos;
using MediatR;

namespace Izigo.Application.Features.Driver.Commands;

// ── POST /driver/onboarding/documents ─────────────────────────────────────────
// Stores a KYC file and returns the file_url the onboarding steps take. Kept apart
// from /me/photo so a licence or insurance scan never replaces the profile photo.

public record UploadKycDocumentCommand(
    string UserId,
    Stream FileStream,
    string ContentType,
    long FileSizeBytes
) : IRequest<KycDocumentUploadResult>;

public class UploadKycDocumentHandler(IFileStorageService storage)
    : IRequestHandler<UploadKycDocumentCommand, KycDocumentUploadResult>
{
    private const long MaxBytes = 10 * 1024 * 1024;   // 10 MB

    public async Task<KycDocumentUploadResult> Handle(UploadKycDocumentCommand req, CancellationToken ct)
    {
        if (req.FileSizeBytes > MaxBytes)
            throw new ArgumentException("VALIDATION_ERROR: Document must be under 10 MB.");

        var ext = req.ContentType.ToLower() switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/png"                 => ".png",
            "image/webp"                => ".webp",
            "application/pdf"           => ".pdf",
            _ => throw new ArgumentException(
                "VALIDATION_ERROR: Only JPEG, PNG, WebP and PDF documents are accepted.")
        };

        var key = $"kyc/{req.UserId}/{Guid.NewGuid():N}{ext}";
        var url = await storage.UploadAsync(req.FileStream, key, req.ContentType, ct);
        return new KycDocumentUploadResult(url);
    }
}
