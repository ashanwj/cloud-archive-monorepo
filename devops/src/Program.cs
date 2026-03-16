using Amazon.CDK;
using CloudArchiveDevops;

var app = new App();

var env = new Amazon.CDK.Environment
{
    Account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"),
    Region  = "ap-southeast-2"
};

var infra   = new InfraStack(app, "CloudArchiveInfraStack",   new StackProps { Env = env });
var compute = new ComputeStack(app, "CloudArchiveComputeStack", infra, new StackProps { Env = env });
compute.AddDependency(infra);

app.Synth();
