namespace CloudArchive.Api.Models;

public record DocumentResponse(
    string   DocumentId,
    string   FileName,
    string   S3Key,
    string   Summary,
    DateTime CreatedAt
);
