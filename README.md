# CloudArchive

A full-stack document archiving application. Upload documents, store them in S3, generate AI summaries via Amazon Bedrock (Claude 3.5 Sonnet), and persist metadata in DynamoDB.

## Architecture

```
web/          Next.js 15 (App Router, SSR)        → AWS Amplify Hosting
api/          ASP.NET Core 10 (Minimal APIs)       → AWS App Runner
devops/       AWS CDK in C# (two-stack)            → InfraStack + ComputeStack
```

**AWS services:** S3 (storage) · DynamoDB (metadata) · Bedrock (summaries) · App Runner (API hosting) · Amplify (frontend hosting) · ECR (Docker images)

**Region:** `ap-southeast-2` · **Account:** `722141136946`

---

## Prerequisites

| Tool | Version |
|---|---|
| .NET SDK | 10.0 |
| Node.js | 22 |
| Docker Desktop | any recent |
| AWS CDK CLI | `npm install -g aws-cdk` |
| AWS CLI | v2 |
| AWS profile | `cloudarchive` configured via `aws configure --profile cloudarchive` |

---

## Running Locally

### 1. API

The API reads AWS credentials from the `cloudarchive` named profile (configured in `api/appsettings.Development.json`). Real AWS services (S3, DynamoDB, Bedrock) are used even in local development.

```bash
cd api
dotnet run
```

API listens on `http://localhost:54331`. Test the upload endpoint:

```bash
curl -F "file=@yourfile.txt" http://localhost:54331/api/documents/upload
```

### 2. Frontend

In a separate terminal:

```bash
cd web
npm ci
npm run dev
```

Frontend runs on `http://localhost:3000`. The `next.config.ts` proxy rewrites all `/api/*` calls to `http://localhost:54331` so the frontend talks to the local API automatically — no environment variables required.

---

## Manual AWS Deployment

Run all PowerShell commands from the repo root unless stated otherwise.

### Step 0 — Set CDK environment variables (required every PowerShell session)

```powershell
$env:CDK_DEFAULT_ACCOUNT = "722141136946"
$env:CDK_DEFAULT_REGION  = "ap-southeast-2"
```

### Step 1 — Store GitHub Personal Access Token in SSM (one-time)

Amplify needs a GitHub PAT to clone the repository during builds.

```powershell
aws ssm put-parameter `
  --name /cloudarchive/github-token `
  --value "ghp_yourtoken" `
  --type String `
  --region ap-southeast-2 `
  --profile cloudarchive
```

### Step 2 — Create GitHub OIDC provider in IAM (one-time)

Allows GitHub Actions to authenticate with AWS without storing long-lived credentials.

```powershell
aws iam create-open-id-connect-provider `
  --url https://token.actions.githubusercontent.com `
  --client-id-list sts.amazonaws.com `
  --thumbprint-list 6938fd4d98bab03faadb97b34396831e3780aea1 `
  --profile cloudarchive
```

> If you see `EntityAlreadyExists` the provider is already there — continue to the next step.

### Step 3 — Deploy InfraStack

Creates S3, DynamoDB, ECR, and IAM roles (including the GitHub Actions OIDC role).

```powershell
cd devops
cdk deploy CloudArchiveInfraStack --profile cloudarchive
```

### Step 4 — Build and push Docker image to ECR

```powershell
# Authenticate Docker with ECR
aws ecr get-login-password --region ap-southeast-2 --profile cloudarchive `
  | docker login --username AWS --password-stdin `
    722141136946.dkr.ecr.ap-southeast-2.amazonaws.com

# Build, tag, and push
docker build -t cloudarchive-api ./api
docker tag cloudarchive-api:latest `
  722141136946.dkr.ecr.ap-southeast-2.amazonaws.com/cloudarchive-api:latest
docker push `
  722141136946.dkr.ecr.ap-southeast-2.amazonaws.com/cloudarchive-api:latest
```

> The image must exist in ECR before ComputeStack is deployed, otherwise App Runner has nothing to pull.

### Step 5 — Deploy ComputeStack

Creates the App Runner service and Amplify app.

```powershell
cdk deploy CloudArchiveComputeStack --profile cloudarchive
```

Note the outputs printed at the end — you will need them in steps 6 and 7.

### Step 6 — Trigger the first App Runner deployment

```powershell
aws apprunner start-deployment `
  --service-arn arn:aws:apprunner:ap-southeast-2:722141136946:service/cloudarchive-api/d106a80a3dba48039881f76c32c75fde `
  --profile cloudarchive
```

Check the deployment status:

```powershell
aws apprunner describe-service `
  --service-arn arn:aws:apprunner:ap-southeast-2:722141136946:service/cloudarchive-api/d106a80a3dba48039881f76c32c75fde `
  --query 'Service.Status' `
  --output text `
  --profile cloudarchive
```

Wait until it returns `RUNNING`.

### Step 7 — Trigger the first Amplify build

```powershell
aws amplify start-job `
  --app-id d20hw7pj39tnza `
  --branch-name main `
  --job-type RELEASE `
  --profile cloudarchive
```

Monitor progress in the [Amplify console](https://ap-southeast-2.console.aws.amazon.com/amplify/home) or via:

```powershell
aws amplify list-jobs `
  --app-id d20hw7pj39tnza `
  --branch-name main `
  --profile cloudarchive
```

---

## CI/CD Pipeline

Pipelines live in [.github/workflows/](.github/workflows/). No AWS credentials are stored in GitHub — authentication uses OIDC (the IAM role created in Step 2 and 3 above).

### CI — runs on every push and every pull request

**File:** [.github/workflows/ci.yml](.github/workflows/ci.yml)

Both jobs run in parallel:

| Job | What it does |
|---|---|
| `Build API` | `dotnet build` + Docker build from `api/` |
| `Build Web` | `npm ci` + `npm run build` from `web/` |

This validates every commit on every branch before it can be merged.

### Deploy — runs only on push to `main`

**File:** [.github/workflows/deploy.yml](.github/workflows/deploy.yml)

Jobs run in sequence — `deploy-web` only starts after `deploy-api` succeeds:

```
push to main
    │
    ▼
 deploy-api
  1. Authenticate via OIDC (no stored credentials)
  2. Login to ECR
  3. Build Docker image and push to ECR (tagged with commit SHA + latest)
  4. Trigger App Runner deployment
    │
    └─ SUCCESS
          │
          ▼
       deploy-web
        1. Authenticate via OIDC
        2. Trigger Amplify build (aws amplify start-job)
```

Amplify `EnableAutoBuild` is disabled — Amplify only builds when GitHub Actions explicitly triggers it, guaranteeing the frontend is never deployed unless the API deploy succeeded first.

### Developer workflow

```
# Create a feature branch
git checkout -b feature/my-change

# Make changes, then push
git push origin feature/my-change
# ↑ CI runs automatically (build-api + build-web in parallel)

# Open a PR to main — CI runs again on the PR
# Merge the PR to main
# ↑ Deploy pipeline runs automatically:
#   1. API image built and pushed to ECR
#   2. App Runner deployment triggered
#   3. Amplify build triggered (only after App Runner step passes)
```

---

## Project Structure

```
cloud-archive-monorepo/
├── api/                          ASP.NET Core 10 API
│   ├── Models/
│   │   ├── Result.cs             Result<T> pattern (no exceptions for logic)
│   │   └── DocumentResponse.cs   Response DTO
│   ├── Services/
│   │   ├── IDocumentService.cs
│   │   └── DocumentService.cs    S3 → Bedrock → DynamoDB
│   ├── Program.cs                Minimal API entry point
│   ├── appsettings.json          Shared config (bucket name, table name)
│   └── appsettings.Development.json  Local-only (AWS profile name)
│
├── web/                          Next.js 15 frontend
│   ├── app/
│   │   ├── page.tsx              Upload page ("use client")
│   │   ├── types.ts              Shared TypeScript types
│   │   └── components/
│   │       ├── DropZone.tsx      Drag-and-drop file input
│   │       └── ResultCard.tsx    Displays summary + metadata
│   └── next.config.ts            API proxy rewrite rules
│
├── devops/                       AWS CDK (C#)
│   └── src/
│       ├── InfraStack.cs         S3, DynamoDB, ECR, IAM roles, OIDC
│       └── ComputeStack.cs       App Runner, Amplify
│
└── .github/
    └── workflows/
        ├── ci.yml                Build on every push / PR
        └── deploy.yml            Deploy on merge to main
```

---

## Key Configuration Values

| Item | Value |
|---|---|
| AWS Region | `ap-southeast-2` |
| AWS Account | `722141136946` |
| S3 Bucket | `poc-cloudarchive-documents-722141136946-ap-southeast-2` |
| DynamoDB Table | `poc-cloudarchive-metadata` |
| ECR Repository | `cloudarchive-api` |
| App Runner Service ARN | `arn:aws:apprunner:ap-southeast-2:722141136946:service/cloudarchive-api/d106a80a3dba48039881f76c32c75fde` |
| Amplify App ID | `d20hw7pj39tnza` |
| GitHub Actions IAM Role | `arn:aws:iam::722141136946:role/cloudarchive-github-actions` |
| Bedrock Model | `anthropic.claude-3-5-sonnet-20241022-v2:0` |
