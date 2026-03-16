using Amazon.CDK;
using Amazon.CDK.AWS.Amplify;
using Amazon.CDK.AWS.AppRunner;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.SSM;
using Constructs;

namespace CloudArchiveDevops;

public sealed class ComputeStack : Stack
{
    public ComputeStack(Construct scope, string id, InfraStack infra, IStackProps? props = null)
        : base(scope, id, props)
    {
        const string region = "ap-southeast-2";

        // ── App Runner Service (L1 CfnService — stable) ───────────────────────────
        // AutoDeploymentsEnabled = false: prevents App Runner from continuously
        // polling ECR. The first real deployment is triggered manually via
        // `aws apprunner start-deployment` after the Docker image is pushed.
        //
        // ASP.NET Core env var separator: AWS:BucketName → AWS__BucketName
        // (double underscore is the .NET configuration hierarchy separator for env vars)
        var appRunner = new CfnService(this, "ApiService", new CfnServiceProps
        {
            ServiceName = "cloudarchive-api",

            SourceConfiguration = new CfnService.SourceConfigurationProperty
            {
                AutoDeploymentsEnabled = false,

                AuthenticationConfiguration = new CfnService.AuthenticationConfigurationProperty
                {
                    AccessRoleArn = infra.AppRunnerAccessRole.RoleArn
                },

                ImageRepository = new CfnService.ImageRepositoryProperty
                {
                    // RepositoryUri is a CDK token — no account ID in source control
                    ImageIdentifier     = $"{infra.ApiEcrRepo.RepositoryUri}:latest",
                    ImageRepositoryType = "ECR",

                    ImageConfiguration = new CfnService.ImageConfigurationProperty
                    {
                        Port = "8080",
                        RuntimeEnvironmentVariables = new[]
                        {
                            new CfnService.KeyValuePairProperty { Name = "ASPNETCORE_URLS",      Value = "http://+:8080"                        },
                            new CfnService.KeyValuePairProperty { Name = "AWS__Region",          Value = region                                 },
                            new CfnService.KeyValuePairProperty { Name = "AWS__BucketName",      Value = infra.DocumentsBucket.BucketName       },
                            new CfnService.KeyValuePairProperty { Name = "AWS__DynamoTableName", Value = infra.MetadataTable.TableName          }
                        }
                    }
                }
            },

            InstanceConfiguration = new CfnService.InstanceConfigurationProperty
            {
                InstanceRoleArn = infra.AppRunnerInstanceRole.RoleArn,
                Cpu             = "1 vCPU",
                Memory          = "2 GB"
            }
        });

        // AttrServiceUrl has no scheme — prepend https://
        var appRunnerUrl = $"https://{appRunner.AttrServiceUrl}";

        // ── GitHub OAuth Token from SSM Parameter Store ────────────────────────────
        // Store before running `cdk synth`:
        //   aws ssm put-parameter --name /cloudarchive/github-token \
        //     --value "ghp_..." --type SecureString \
        //     --region ap-southeast-2 --profile cloudarchive
        //
        // ValueFromLookup resolves at synth time (not deploy time).
        // The parameter must exist in SSM before the first `cdk synth`.
        var githubToken = StringParameter.ValueFromLookup(this, "/cloudarchive/github-token");

        // ── Amplify IAM Service Role ───────────────────────────────────────────────
        // Required for WEB_COMPUTE (SSR) mode. Amplify uses this role to manage
        // the compute resources that serve Next.js server-side rendered pages.
        var amplifyRole = new Role(this, "AmplifyServiceRole", new RoleProps
        {
            RoleName        = "cloudarchive-amplify-service",
            AssumedBy       = new ServicePrincipal("amplify.amazonaws.com"),
            ManagedPolicies = new[]
            {
                ManagedPolicy.FromAwsManagedPolicyName("AdministratorAccess-Amplify")
            }
        });

        // ── Amplify App (L1 CfnApp) ───────────────────────────────────────────────
        // Platform = "WEB_COMPUTE" is mandatory for Next.js 15 App Router SSR.
        // Without it, Amplify falls back to static export mode and all dynamic
        // routes and server actions return 404.
        //
        // Build spec targets the /web subdirectory of the monorepo.
        // baseDirectory = web/.next matches where `next build` places SSR output.
        const string buildSpec = @"version: 1
applications:
  - frontend:
      phases:
        preBuild:
          commands:
            - cd web && npm ci
        build:
          commands:
            - npm run build
      artifacts:
        baseDirectory: web/.next
        files:
          - '**/*'
      cache:
        paths:
          - web/node_modules/**/*
      appRoot: web";

        var amplifyApp = new CfnApp(this, "AmplifyApp", new CfnAppProps
        {
            Name           = "cloudarchive-web",
            Platform       = "WEB_COMPUTE",
            OauthToken     = githubToken,
            Repository     = "https://github.com/YOUR_ORG/cloud-archive-monorepo", // ← replace before deploy
            IamServiceRole = amplifyRole.RoleArn,
            BuildSpec      = buildSpec,

            EnvironmentVariables = new[]
            {
                // Consumed by next.config.ts rewrites() at SSR server startup.
                // Points Next.js to the App Runner URL instead of localhost.
                new CfnApp.EnvironmentVariableProperty { Name = "DOTNET_API_URL", Value = appRunnerUrl }
            }
        });

        _ = new CfnBranch(this, "MainBranch", new CfnBranchProps
        {
            AppId           = amplifyApp.AttrAppId,
            BranchName      = "main",
            Stage           = "PRODUCTION",
            Framework       = "Next.js - SSR",
            EnableAutoBuild = true,

            EnvironmentVariables = new[]
            {
                new CfnBranch.EnvironmentVariableProperty { Name = "DOTNET_API_URL", Value = appRunnerUrl }
            }
        });

        // ── Outputs ───────────────────────────────────────────────────────────────
        _ = new CfnOutput(this, "AppRunnerUrl", new CfnOutputProps
        {
            Value       = appRunnerUrl,
            Description = "App Runner URL — update Amplify DOTNET_API_URL to this value"
        });

        _ = new CfnOutput(this, "AmplifyAppId", new CfnOutputProps
        {
            Value       = amplifyApp.AttrAppId,
            Description = "Use with `aws amplify start-job` to trigger a build"
        });

        _ = new CfnOutput(this, "AmplifyUrl", new CfnOutputProps
        {
            Value       = $"https://main.{amplifyApp.AttrDefaultDomain}",
            Description = "Live frontend URL"
        });
    }
}
