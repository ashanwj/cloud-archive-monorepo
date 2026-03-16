# Project: CloudArchive (.NET 10 + Next.js)

## Tech Stack
- Backend: ASP.NET Core 10 (Minimal APIs)
- Frontend: Next.js 15 (App Router, TypeScript)
- AWS: S3 (Storage), DynamoDB (Metadata), Bedrock (Claude 3.5 Sonnet)
- Infrastructure: AWS CDK (C#)

## Coding Standards
- Use the **Result Pattern** for API responses (no throwing exceptions for logic).
- Use **Minimal APIs** with `StandardEndpoints` or `RouteGroups`.
- Dependency Injection: Prefer `Scoped` for services.
- Naming: PascalCase for C#, camelCase for TypeScript.

## Agentic Workflow
- When asked to "Implement a feature," always check if an AWS Service Client is needed.
- If a new NuGet package is required, ask for permission before running `dotnet add package`.