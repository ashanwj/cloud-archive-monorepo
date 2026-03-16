using Amazon.BedrockRuntime;
using Amazon.DynamoDBv2;
using Amazon.S3;
using CloudArchive.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// --- 1. AWS Services Registration ---
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonS3>();
builder.Services.AddAWSService<IAmazonBedrockRuntime>();
builder.Services.AddAWSService<IAmazonDynamoDB>();

// --- 2. Custom Services ---
builder.Services.AddScoped<IDocumentService, DocumentService>();

var app = builder.Build();

// --- 3. Minimal API Endpoints ---
var docs = app.MapGroup("/api/documents");

docs.MapPost("/upload", async (IFormFile file, IDocumentService docService) =>
{
    if (file == null || file.Length == 0) return Results.BadRequest("Invalid file.");
    var result = await docService.ProcessDocumentAsync(file);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.Problem(result.Error);
}).DisableAntiforgery();

app.Run();