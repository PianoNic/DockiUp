using DockiUp.Application.Commands;
using DockiUp.Application.Dtos;
using DockiUp.Application.Validators;
using DockiUp.Domain.Enums;

namespace DockiUp.Tests.Application;

public class DeployProjectCommandValidatorTests
{
    private readonly DeployProjectCommandValidator _validator = new();

    private static DeployProjectCommand Cmd(Action<SetupProjectDto> tweak)
    {
        var dto = new SetupProjectDto
        {
            ProjectName = "valid",
            ProjectOrigin = ProjectOriginType.Compose,
            Compose = "services: {}",
            ProjectUpdateMethod = ProjectUpdateMethod.Manual,
        };
        tweak(dto);
        return new DeployProjectCommand(dto);
    }

    [Fact]
    public void ValidCompose_Passes()
        => Assert.True(_validator.Validate(Cmd(_ => { })).IsValid);

    [Fact]
    public void MissingProjectName_Fails()
        => Assert.False(_validator.Validate(Cmd(d => d.ProjectName = "")).IsValid);

    [Fact]
    public void TooLongProjectName_Fails()
        => Assert.False(_validator.Validate(Cmd(d => d.ProjectName = new string('x', 101))).IsValid);

    [Fact]
    public void TooLongDescription_Fails()
        => Assert.False(_validator.Validate(Cmd(d => d.Description = new string('x', 501))).IsValid);

    [Fact]
    public void InvalidProjectOrigin_Fails()
        => Assert.False(_validator.Validate(Cmd(d => d.ProjectOrigin = (ProjectOriginType)999)).IsValid);

    [Fact]
    public void Compose_WithoutComposeContent_Fails()
        => Assert.False(_validator.Validate(Cmd(d => d.Compose = null)).IsValid);

    [Fact]
    public void Git_RequiresGitUrl()
    {
        Assert.False(_validator.Validate(Cmd(d => { d.ProjectOrigin = ProjectOriginType.Git; d.GitUrl = null; })).IsValid);
        Assert.True(_validator.Validate(Cmd(d => { d.ProjectOrigin = ProjectOriginType.Git; d.GitUrl = "file:///r.git"; d.Compose = "services: {}"; })).IsValid);
    }

    [Fact]
    public void Adopted_IsNotCreatedThroughDeployProject()
        => Assert.False(_validator.Validate(Cmd(d => d.ProjectOrigin = ProjectOriginType.Adopted)).IsValid);

    [Fact]
    public void TooLongGitUrl_Fails()
        => Assert.False(_validator.Validate(Cmd(d => { d.ProjectOrigin = ProjectOriginType.Git; d.GitUrl = new string('x', 2001); })).IsValid);

    [Fact]
    public void TooLongCompose_Fails()
        => Assert.False(_validator.Validate(Cmd(d => d.Compose = new string('x', 100_001))).IsValid);

    [Fact]
    public void LargeComposeUpTo100k_Passes()
        => Assert.True(_validator.Validate(Cmd(d => d.Compose = "services: {}" + new string(' ', 50_000))).IsValid);

    [Fact]
    public void TooLongEnvFile_Fails()
        => Assert.False(_validator.Validate(Cmd(d => d.EnvFile = new string('x', 50_001))).IsValid);

    [Fact]
    public void InvalidUpdateMethod_Fails()
        => Assert.False(_validator.Validate(Cmd(d => d.ProjectUpdateMethod = (ProjectUpdateMethod)999)).IsValid);

    [Fact]
    public void Webhook_NeedsNoUserSuppliedUrl()
        => Assert.True(_validator.Validate(Cmd(d => d.ProjectUpdateMethod = ProjectUpdateMethod.Webhook)).IsValid);

    [Theory]
    [InlineData("../etc")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData(@"a\b")]
    [InlineData(" leading-space")]
    [InlineData("-dash-first")]
    [InlineData("semi;colon")]
    public void ProjectName_UnsafeForFolderOrComposeName_Fails(string name)
        => Assert.False(_validator.Validate(Cmd(d => d.ProjectName = name)).IsValid);

    [Theory]
    [InlineData("My App_1")]
    [InlineData("web-2")]
    public void ProjectName_Safe_Passes(string name)
        => Assert.True(_validator.Validate(Cmd(d => d.ProjectName = name)).IsValid);

    [Theory]
    [InlineData("../x.yml")]
    [InlineData("deploy/../../x.yml")]
    [InlineData("/etc/compose.yml")]
    public void Git_ComposeFileOutsideRepo_Fails(string composeFile)
        => Assert.False(_validator.Validate(Cmd(d =>
        {
            d.ProjectOrigin = ProjectOriginType.Git; d.GitUrl = "https://git/x.git"; d.Compose = null; d.ComposeFile = composeFile;
        })).IsValid);

    [Theory]
    [InlineData(null)]
    [InlineData("docker-compose.yml")]
    [InlineData("deploy/compose.prod.yml")]
    public void Git_ComposeFileInsideRepo_OrDefault_Passes_WithoutPastedCompose(string? composeFile)
        => Assert.True(_validator.Validate(Cmd(d =>
        {
            d.ProjectOrigin = ProjectOriginType.Git; d.GitUrl = "https://git/x.git"; d.Compose = null; d.ComposeFile = composeFile;
        })).IsValid);

    [Fact]
    public void Periodic_RequiresPositiveInterval()
    {
        Assert.False(_validator.Validate(Cmd(d => d.ProjectUpdateMethod = ProjectUpdateMethod.Periodically)).IsValid);
        Assert.False(_validator.Validate(Cmd(d => { d.ProjectUpdateMethod = ProjectUpdateMethod.Periodically; d.PeriodicIntervalInMinutes = 0; })).IsValid);
        Assert.True(_validator.Validate(Cmd(d => { d.ProjectUpdateMethod = ProjectUpdateMethod.Periodically; d.PeriodicIntervalInMinutes = 5; })).IsValid);
    }

    [Fact]
    public void NonPeriodic_WithInterval_Fails()
        => Assert.False(_validator.Validate(Cmd(d => { d.ProjectUpdateMethod = ProjectUpdateMethod.Manual; d.PeriodicIntervalInMinutes = -1; })).IsValid);
}
