using FluentAssertions;
using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Driver.Commands;
using Moq;
using Xunit;

namespace Izigo.Application.Tests.Features.Driver;

public class UploadKycDocumentTests
{
    private static (UploadKycDocumentHandler Handler, Mock<IFileStorageService> Storage) Build()
    {
        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string key, string _, CancellationToken _) => $"/uploads/{key}");
        return (new UploadKycDocumentHandler(storage.Object), storage);
    }

    [Theory]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/png", ".png")]
    [InlineData("application/pdf", ".pdf")]
    public async Task Upload_StoresUnderTheDriversKycFolder(string contentType, string ext)
    {
        var (handler, _) = Build();

        var result = await handler.Handle(
            new UploadKycDocumentCommand("user1", new MemoryStream([1, 2, 3]), contentType, 3), default);

        result.FileUrl.Should().StartWith("/uploads/kyc/user1/").And.EndWith(ext);
    }

    [Fact]
    public async Task Upload_RejectsUnsupportedTypes()
    {
        var (handler, storage) = Build();

        var act = () => handler.Handle(
            new UploadKycDocumentCommand("user1", new MemoryStream([1]), "text/plain", 1), default);

        await act.Should().ThrowAsync<ArgumentException>();
        storage.Verify(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Upload_RejectsFilesOver10Mb()
    {
        var (handler, _) = Build();

        var act = () => handler.Handle(
            new UploadKycDocumentCommand("user1", new MemoryStream(), "image/jpeg", 10 * 1024 * 1024 + 1), default);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
