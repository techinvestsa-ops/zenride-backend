using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Profile.Dtos;
using MediatR;

namespace Izigo.Application.Features.Profile.Commands;

public record UploadPhotoCommand(
    string UserId,
    Stream FileStream,
    string FileName,
    string ContentType,
    long FileSizeBytes
) : IRequest<PhotoUploadResult>;

public class UploadPhotoHandler(IApplicationDbContext db, IFileStorageService storage)
    : IRequestHandler<UploadPhotoCommand, PhotoUploadResult>
{
    private const long MaxBytes = 5 * 1024 * 1024;   // 5 MB
    private static readonly string[] AllowedTypes = ["image/jpeg", "image/png", "image/webp"];

    public async Task<PhotoUploadResult> Handle(UploadPhotoCommand req, CancellationToken ct)
    {
        if (req.FileSizeBytes > MaxBytes)
            throw new ArgumentException("VALIDATION_ERROR: Photo must be under 5 MB.");

        if (!AllowedTypes.Contains(req.ContentType.ToLower()))
            throw new ArgumentException("VALIDATION_ERROR: Only JPEG, PNG and WebP photos are accepted.");

        var user = await db.Users.FindAsync([req.UserId], ct)
            ?? throw new KeyNotFoundException("User not found.");

        var ext = req.ContentType.ToLower() switch
        {
            "image/png"  => ".png",
            "image/webp" => ".webp",
            _            => ".jpg"
        };

        var key = $"avatars/{req.UserId}/{Guid.NewGuid():N}{ext}";
        var url = await storage.UploadAsync(req.FileStream, key, req.ContentType, ct);

        user.PhotoUrl = url;
        await db.SaveChangesAsync(ct);

        return new PhotoUploadResult(url);
    }
}
