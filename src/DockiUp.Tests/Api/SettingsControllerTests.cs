using DockiUp.API.Controllers;
using DockiUp.API.Settings;
using DockiUp.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;

namespace DockiUp.Tests.Api;

/// <summary>Settings saved in the UI reach IConfiguration below the environment, which locks them.</summary>
public class SettingsControllerTests
{
    private readonly DockiUp.Infrastructure.DockiUpDbContext _db = TestSupport.TestDb.Create();
    private readonly DatabaseSettingsProvider _provider = new();
    private readonly IConfigurationRoot _config;

    public SettingsControllerTests()
    {
        var builder = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ImageUpdates:Interval"] = "6h" });
        builder.Sources.Add(_provider);
        _config = builder.Build();
    }

    private SettingsController Controller() => new(_db, _config, _provider, new Mock<IActivityLogger>().Object);

    private static SettingsController.SettingDto Value(ActionResult<SettingsController.SettingDto> r)
        => Assert.IsType<SettingsController.SettingDto>(Assert.IsType<OkObjectResult>(r.Result).Value);

    [Fact]
    public async Task Saving_AppliesAtOnce_AndEmptyGoesBackToDefault()
    {
        Assert.Equal("5m", Value(await Controller().SaveSetting(new("ImageUpdates:Interval", "300s"), default)).Value);
        Assert.Equal("5m", _config["ImageUpdates:Interval"]);

        await Controller().SaveSetting(new("ImageUpdates:Interval", ""), default);
        Assert.Equal("6h", _config["ImageUpdates:Interval"]);
        Assert.Empty(_db.AppSettings);
    }

    [Fact]
    public async Task InvalidValues_AndUnknownKeys_AreRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Controller().SaveSetting(new("Stats:IntervalSeconds", "1"), default)); // below 5
        await Assert.ThrowsAsync<ArgumentException>(() => Controller().SaveSetting(new("DockiUp:PublicUrl", "ftp://x"), default));
        await Assert.ThrowsAsync<ArgumentException>(() => Controller().SaveSetting(new("ImageUpdates:Interval", "2s"), default)); // below 5s
        await Assert.ThrowsAsync<ArgumentException>(() => Controller().SaveSetting(new("Oidc:Authority", "https://evil"), default));
    }

    [Fact]
    public void TheLayer_SitsAboveAppSettingsJson_AndBelowTheEnvironment()
    {
        Environment.SetEnvironmentVariable("STATS__INTERVALSECONDS", "30");
        try
        {
            var config = new ConfigurationManager();
            config.AddEnvironmentVariables("ASPNETCORE_"); // WebApplicationBuilder adds prefixed ones first
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["ImageUpdates:Interval"] = "6h", ["Stats:IntervalSeconds"] = "15" }); // appsettings.json
            config.AddEnvironmentVariables();
            DatabaseSettingsProvider.AddTo(config).Set([new() { Key = "ImageUpdates:Interval", Value = "12h" }, new() { Key = "Stats:IntervalSeconds", Value = "20" }]);

            Assert.Equal("12h", config["ImageUpdates:Interval"]);
            Assert.Equal("30", config["Stats:IntervalSeconds"]);
        }
        finally { Environment.SetEnvironmentVariable("STATS__INTERVALSECONDS", null); }
    }

    [Fact]
    public async Task ASettingFromTheEnvironment_IsLocked()
    {
        Environment.SetEnvironmentVariable("STATS__RETENTIONHOURS", "48");
        try
        {
            var dto = Assert.IsType<SettingsController.SettingDto[]>(Assert.IsType<OkObjectResult>(Controller().ListSettings().Result).Value)
                .Single(s => s.Key == "Stats:RetentionHours");
            Assert.Equal("Stats__RetentionHours", dto.LockedBy);
            await Assert.ThrowsAsync<ArgumentException>(() => Controller().SaveSetting(new("Stats:RetentionHours", "2"), default));
        }
        finally { Environment.SetEnvironmentVariable("STATS__RETENTIONHOURS", null); }
    }
}
