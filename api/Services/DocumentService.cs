using System.Text;
using System.Text.Json;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.S3;
using Amazon.S3.Model;
using CloudArchive.Api.Models;

namespace CloudArchive.Api.Services;

public sealed class DocumentService : IDocumentService
{
    private const string BedrockModelId = "anthropic.claude-3-5-sonnet-20241022-v2:0";

    private readonly IAmazonS3                _s3;
    private readonly IAmazonDynamoDB          _dynamo;
    private readonly IAmazonBedrockRuntime    _bedrock;
    private readonly ILogger<DocumentService> _logger;
    private readonly string                   _bucketName;
    private readonly string                   _tableName;

    public DocumentService(
        IAmazonS3 s3,
        IAmazonDynamoDB dynamo,
        IAmazonBedrockRuntime bedrock,
        IConfiguration config,
        ILogger<DocumentService> logger)
    {
        _s3         = s3;
        _dynamo     = dynamo;
        _bedrock    = bedrock;
        _logger     = logger;
        _bucketName = config["AWS:BucketName"]
                      ?? throw new InvalidOperationException("AWS:BucketName is not configured.");
        _tableName  = config["AWS:DynamoTableName"]
                      ?? throw new InvalidOperationException("AWS:DynamoTableName is not configured.");
    }

    public async Task<Result<DocumentResponse>> ProcessDocumentAsync(IFormFile file)
    {
        try
        {
            var documentId = Guid.NewGuid().ToString();
            var s3Key      = $"documents/{documentId}/{file.FileName}";
            var createdAt  = DateTime.UtcNow;

            var upload = await UploadToS3Async(file, s3Key);
            if (!upload.IsSuccess) return Result<DocumentResponse>.Fail(upload.Error!);

            string text;
            using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8))
                text = await reader.ReadToEndAsync();

            var summary = await GenerateSummaryAsync(text);
            if (!summary.IsSuccess) return Result<DocumentResponse>.Fail(summary.Error!);

            var save = await SaveMetadataAsync(documentId, file.FileName, s3Key, summary.Value!, createdAt);
            if (!save.IsSuccess) return Result<DocumentResponse>.Fail(save.Error!);

            return Result<DocumentResponse>.Ok(
                new DocumentResponse(documentId, file.FileName, s3Key, summary.Value!, createdAt));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error processing {FileName}", file.FileName);
            return Result<DocumentResponse>.Fail($"Unexpected error: {ex.Message}");
        }
    }

    //Private methods for S3 upload, Bedrock summary generation, and DynamoDB metadata save, each returning a Result<T> indicating success or failure with error messages logged appropriately.
    private async Task<Result<bool>> UploadToS3Async(IFormFile file, string s3Key)
    {
        try
        {
            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName                 = _bucketName,
                Key                        = s3Key,
                InputStream                = file.OpenReadStream(),
                ContentType                = file.ContentType,
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
            });
            return Result<bool>.Ok(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "S3 upload failed for key {S3Key}", s3Key);
            return Result<bool>.Fail($"S3 upload failed: {ex.Message}");
        }
    }

    private async Task<Result<string>> GenerateSummaryAsync(string text)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                anthropic_version = "bedrock-2023-05-31",
                max_tokens        = 1024,
                messages          = new[]
                {
                    new { role = "user", content = $"Summarize this document concisely:\n\n{text}" }
                }
            });

            var response = await _bedrock.InvokeModelAsync(new InvokeModelRequest
            {
                ModelId     = BedrockModelId,
                ContentType = "application/json",
                Accept      = "application/json",
                Body        = new MemoryStream(Encoding.UTF8.GetBytes(payload))
            });

            using var doc = await JsonDocument.ParseAsync(response.Body);
            var summary   = doc.RootElement
                               .GetProperty("content")[0]
                               .GetProperty("text")
                               .GetString()
                            ?? "Summary unavailable.";

            return Result<string>.Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bedrock invocation failed for {ModelId}", BedrockModelId);
            return Result<string>.Fail($"Bedrock summary failed: {ex.Message}");
        }
    }

    private async Task<Result<bool>> SaveMetadataAsync(
        string documentId, string fileName, string s3Key, string summary, DateTime createdAt)
    {
        try
        {
            await _dynamo.PutItemAsync(new PutItemRequest
            {
                TableName = _tableName,
                Item      = new Dictionary<string, AttributeValue>
                {
                    ["DocumentId"] = new() { S = documentId },
                    ["FileName"]   = new() { S = fileName   },
                    ["S3Key"]      = new() { S = s3Key      },
                    ["Summary"]    = new() { S = summary     },
                    ["CreatedAt"]  = new() { S = createdAt.ToString("O") }
                }
            });
            return Result<bool>.Ok(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DynamoDB write failed for {DocumentId}", documentId);
            return Result<bool>.Fail($"Metadata save failed: {ex.Message}");
        }
    }
}
