using CloudArchive.Api.Models;

namespace CloudArchive.Api.Services;

public interface IDocumentService
{
    Task<Result<DocumentResponse>> ProcessDocumentAsync(IFormFile file);
}
