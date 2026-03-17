using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.ECR;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.S3;
using Constructs;

namespace CloudArchiveDevops;

public sealed class InfraStack : Stack
{
    public Bucket     DocumentsBucket      { get; }
    public Table      MetadataTable        { get; }
    public Repository ApiEcrRepo           { get; }
    public Role       AppRunnerInstanceRole { get; }
    public Role       AppRunnerAccessRole   { get; }

    public InfraStack(Construct scope, string id, IStackProps? props = null)
        : base(scope, id, props)
    {
        const string region       = "ap-southeast-2";
        const string bedrockModel = "anthropic.claude-3-5-sonnet-20241022-v2:0";

        // ── S3 Bucket ─────────────────────────────────────────────────────────────
        DocumentsBucket = new Bucket(this, "DocumentsBucket", new BucketProps
        {
            // Aws.ACCOUNT_ID resolves to CloudFormation's AWS::AccountId at deploy time —
            // the account number never appears in source control.
            BucketName        = $"poc-cloudarchive-documents-{Aws.ACCOUNT_ID}-ap-southeast-2",
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            Encryption        = BucketEncryption.S3_MANAGED,
            EnforceSSL        = true,
            RemovalPolicy     = RemovalPolicy.RETAIN
        });

        // ── DynamoDB Table ────────────────────────────────────────────────────────
        MetadataTable = new Table(this, "MetadataTable", new TableProps
        {
            TableName    = "poc-cloudarchive-metadata",
            PartitionKey = new Amazon.CDK.AWS.DynamoDB.Attribute
            {
                Name = "DocumentId",
                Type = AttributeType.STRING
            },
            BillingMode   = BillingMode.PAY_PER_REQUEST,
            RemovalPolicy = RemovalPolicy.RETAIN
        });

        // ── ECR Repository ────────────────────────────────────────────────────────
        ApiEcrRepo = new Repository(this, "ApiRepository", new RepositoryProps
        {
            RepositoryName = "cloudarchive-api",
            EmptyOnDelete  = true,
            RemovalPolicy  = RemovalPolicy.DESTROY
        });

        // ── IAM Instance Role (App Runner → S3 / DynamoDB / Bedrock) ─────────────
        // tasks.apprunner.amazonaws.com injects temporary credentials into the
        // running container via instance metadata — no named AWS profile required.
        AppRunnerInstanceRole = new Role(this, "AppRunnerInstanceRole", new RoleProps
        {
            RoleName  = "cloudarchive-apprunner-instance",
            AssumedBy = new ServicePrincipal("tasks.apprunner.amazonaws.com")
        });

        // Least-privilege: only PutObject (no GetObject / DeleteObject)
        DocumentsBucket.GrantPut(AppRunnerInstanceRole);

        // Least-privilege: only PutItem (not GrantWriteData which also grants DeleteItem)
        AppRunnerInstanceRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect    = Effect.ALLOW,
            Actions   = new[] { "dynamodb:PutItem" },
            Resources = new[] { MetadataTable.TableArn }
        }));

        // Least-privilege: InvokeModel scoped to Claude 3.5 Sonnet only
        // Bedrock foundation model ARN uses double :: (no account ID segment)
        AppRunnerInstanceRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect    = Effect.ALLOW,
            Actions   = new[] { "bedrock:InvokeModel" },
            Resources = new[]
            {
                $"arn:aws:bedrock:{region}::foundation-model/{bedrockModel}"
            }
        }));

        // ── IAM Access Role (App Runner build pipeline → ECR image pull) ──────────
        AppRunnerAccessRole = new Role(this, "AppRunnerAccessRole", new RoleProps
        {
            RoleName        = "cloudarchive-apprunner-access",
            AssumedBy       = new ServicePrincipal("build.apprunner.amazonaws.com"),
            ManagedPolicies = new[]
            {
                ManagedPolicy.FromAwsManagedPolicyName(
                    "service-role/AWSAppRunnerServicePolicyForECRAccess")
            }
        });

        // ── GitHub Actions OIDC + IAM Role ────────────────────────────────────────
        // Allows GitHub Actions to assume an IAM role via OpenID Connect —
        // no long-lived AWS credentials stored in GitHub secrets.
        var githubOidc = new OpenIdConnectProvider(this, "GitHubOidc", new OpenIdConnectProviderProps
        {
            Url       = "https://token.actions.githubusercontent.com",
            ClientIds = new[] { "sts.amazonaws.com" }
        });

        var githubActionsRole = new Role(this, "GitHubActionsRole", new RoleProps
        {
            RoleName  = "cloudarchive-github-actions",
            AssumedBy = new WebIdentityPrincipal(
                githubOidc.OpenIdConnectProviderArn,
                new Dictionary<string, object>
                {
                    // Scope to this exact repo; :* covers all branches and PR refs
                    ["StringLike"] = new Dictionary<string, string>
                    {
                        ["token.actions.githubusercontent.com:sub"] =
                            "repo:AshanWj/cloud-archive-monorepo:*"
                    },
                    ["StringEquals"] = new Dictionary<string, string>
                    {
                        ["token.actions.githubusercontent.com:aud"] = "sts.amazonaws.com"
                    }
                }
            )
        });

        // ECR: push images to the cloudarchive-api repo only
        ApiEcrRepo.GrantPush(githubActionsRole);

        // App Runner: trigger deployments only — no create/delete/update
        githubActionsRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect    = Effect.ALLOW,
            Actions   = new[] { "apprunner:StartDeployment" },
            // Wildcard on the service ID segment — the ID is only known after first deploy
            Resources = new[] { "arn:aws:apprunner:ap-southeast-2:722141136946:service/cloudarchive-api/*" }
        }));

        // ── Outputs ───────────────────────────────────────────────────────────────
        _ = new CfnOutput(this, "EcrRepositoryUri", new CfnOutputProps
        {
            Value       = ApiEcrRepo.RepositoryUri,
            Description = "docker push target for the .NET API image"
        });

        _ = new CfnOutput(this, "GitHubActionsRoleArn", new CfnOutputProps
        {
            Value       = githubActionsRole.RoleArn,
            Description = "Paste into .github/workflows/deploy.yml GITHUB_ACTIONS_ROLE_ARN"
        });
    }
}
