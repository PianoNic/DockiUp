using DockiUp.Application;
using DockiUp.Application.Commands;
using DockiUp.Application.Dtos;
using DockiUp.Application.Pipeline;
using DockiUp.Application.Validators;
using DockiUp.Domain.Enums;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace DockiUp.Tests.Application;

public class FluentValidationPipelineBehaviorTests
{
    private static DeployProjectCommand Cmd(bool valid) => new(new SetupProjectDto
    {
        ProjectName = valid ? "ok" : "",
        ProjectOrigin = ProjectOriginType.Compose,
        Compose = "services: {}",
        ProjectUpdateMethod = ProjectUpdateMethod.Manual,
    });

    private static FluentValidationPipelineBehavior<DeployProjectCommand, Unit> Behavior(bool withValidator)
    {
        var services = new ServiceCollection();
        if (withValidator)
            services.AddScoped<IValidator<DeployProjectCommand>, DeployProjectCommandValidator>();
        return new FluentValidationPipelineBehavior<DeployProjectCommand, Unit>(services.BuildServiceProvider());
    }

    [Fact]
    public async Task ValidMessage_CallsNext()
    {
        var called = false;
        ValueTask<Unit> Next(DeployProjectCommand _, CancellationToken __) { called = true; return new ValueTask<Unit>(Unit.Value); }

        await Behavior(withValidator: true).Handle(Cmd(valid: true), Next, CancellationToken.None);

        Assert.True(called);
    }

    [Fact]
    public async Task InvalidMessage_ThrowsValidationException()
    {
        ValueTask<Unit> Next(DeployProjectCommand _, CancellationToken __) => new(Unit.Value);
        await Assert.ThrowsAsync<ValidationException>(() =>
            Behavior(withValidator: true).Handle(Cmd(valid: false), Next, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task NoValidatorRegistered_CallsNext()
    {
        var called = false;
        ValueTask<Unit> Next(DeployProjectCommand _, CancellationToken __) { called = true; return new ValueTask<Unit>(Unit.Value); }

        // Even an otherwise-invalid message passes when no validator is registered for it.
        await Behavior(withValidator: false).Handle(Cmd(valid: false), Next, CancellationToken.None);

        Assert.True(called);
    }
}

public class ModuleRegistryTests
{
    [Fact]
    public void AddApplicationModule_RegistersValidatorsAndMediator()
    {
        var services = new ServiceCollection();
        services.AddApplicationModule();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService<IValidator<DeployProjectCommand>>());
        Assert.NotNull(scope.ServiceProvider.GetService<IMediator>());
    }
}
