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
    public void Git_RequiresGitUrlAndCompose()
    {
        Assert.False(_validator.Validate(Cmd(d => { d.ProjectOrigin = ProjectOriginType.Git; d.GitUrl = null; })).IsValid);
        Assert.True(_validator.Validate(Cmd(d => { d.ProjectOrigin = ProjectOriginType.Git; d.GitUrl = "file:///r.git"; d.Compose = "services: {}"; })).IsValid);
    }

    [Fact]
    public void Import_RequiresPath()
    {
        Assert.False(_validator.Validate(Cmd(d => { d.ProjectOrigin = ProjectOriginType.Import; d.Compose = null; })).IsValid);
        Assert.True(_validator.Validate(Cmd(d => { d.ProjectOrigin = ProjectOriginType.Import; d.Compose = null; d.Path = "/opt/app"; })).IsValid);
    }

    [Fact]
    public void TooLongGitUrl_Fails()
        => Assert.False(_validator.Validate(Cmd(d => { d.ProjectOrigin = ProjectOriginType.Git; d.GitUrl = new string('x', 2001); })).IsValid);

    [Fact]
    public void TooLongCompose_Fails()
        => Assert.False(_validator.Validate(Cmd(d => d.Compose = new string('x', 10001))).IsValid);

    [Fact]
    public void TooLongPath_Fails()
        => Assert.False(_validator.Validate(Cmd(d => { d.ProjectOrigin = ProjectOriginType.Import; d.Compose = null; d.Path = new string('x', 501); })).IsValid);

    [Fact]
    public void InvalidUpdateMethod_Fails()
        => Assert.False(_validator.Validate(Cmd(d => d.ProjectUpdateMethod = (ProjectUpdateMethod)999)).IsValid);

    [Fact]
    public void Webhook_RequiresWebhookUrl()
    {
        Assert.False(_validator.Validate(Cmd(d => d.ProjectUpdateMethod = ProjectUpdateMethod.Webhook)).IsValid);
        Assert.True(_validator.Validate(Cmd(d => { d.ProjectUpdateMethod = ProjectUpdateMethod.Webhook; d.WebhookUrl = "https://hook"; })).IsValid);
    }

    [Fact]
    public void Periodic_RequiresPositiveInterval()
    {
        Assert.False(_validator.Validate(Cmd(d => d.ProjectUpdateMethod = ProjectUpdateMethod.Periodically)).IsValid);
        Assert.False(_validator.Validate(Cmd(d => { d.ProjectUpdateMethod = ProjectUpdateMethod.Periodically; d.PeriodicIntervalInMinutes = 0; })).IsValid);
        Assert.True(_validator.Validate(Cmd(d => { d.ProjectUpdateMethod = ProjectUpdateMethod.Periodically; d.PeriodicIntervalInMinutes = 5; })).IsValid);
    }

    [Fact]
    public void TooLongWebhookUrl_Fails()
        => Assert.False(_validator.Validate(Cmd(d => { d.ProjectUpdateMethod = ProjectUpdateMethod.Webhook; d.WebhookUrl = new string('x', 2001); })).IsValid);

    [Fact]
    public void NonPeriodic_WithInterval_Fails()
        => Assert.False(_validator.Validate(Cmd(d => { d.ProjectUpdateMethod = ProjectUpdateMethod.Manual; d.PeriodicIntervalInMinutes = -1; })).IsValid);
}
