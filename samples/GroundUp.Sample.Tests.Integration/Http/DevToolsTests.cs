using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Services.Invitations;
using GroundUp.Core.Entities.Settings;
using GroundUp.Data.Abstractions;
using GroundUp.Sample.Controllers;
using GroundUp.Sample.Data;
using GroundUp.Sample.Dev;
using GroundUp.Sample.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;

namespace GroundUp.Sample.Tests.Integration.Http;

/// <summary>
/// GU-90: the sample's Development tools: the dev mailbox (sender and endpoint), the accept-URL
/// seed, and that none of it is registered outside Development. The factory runs in Development.
/// </summary>
[Collection("Api")]
public sealed class DevToolsTests
{
    private readonly SampleApiFactory _factory;

    public DevToolsTests(SampleApiFactory factory)
    {
        _factory = factory;
    }

    // ── Registration ──────────────────────────────────────────────

    [Fact]
    public void AddSampleDevTools_OutsideDevelopment_RegistersNothing()
    {
        var services = new ServiceCollection();

        services.AddSampleDevTools(new HostingEnvironment { EnvironmentName = Environments.Production });

        services.Should().BeEmpty();
    }

    [Fact]
    public void Development_ReplacesTheInvitationEmailSender()
    {
        using var scope = _factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IInvitationEmailSender>().Should().BeOfType<DevMailboxInvitationEmailSender>();
        scope.ServiceProvider.GetServices<IDataSeeder>().Should().ContainSingle(s => s is DevSettingsSeeder);
    }

    // ── Mailbox ───────────────────────────────────────────────────

    [Fact]
    public void DevMailbox_KeepsTheNewestFifty_NewestFirst()
    {
        var mailbox = new DevMailbox();

        for (var i = 1; i <= DevMailbox.Capacity + 5; i++)
        {
            mailbox.Add(Email($"user{i}@example.com"));
        }

        var kept = mailbox.List();
        kept.Should().HaveCount(DevMailbox.Capacity);
        kept[0].To.Should().Be($"user{DevMailbox.Capacity + 5}@example.com");
        kept[^1].To.Should().Be("user6@example.com");
    }

    [Fact]
    public async Task Sender_KeepsTheEmailInTheMailbox()
    {
        var mailbox = new DevMailbox();
        var sender = new DevMailboxInvitationEmailSender(mailbox, TimeProvider.System, NullLogger<DevMailboxInvitationEmailSender>.Instance);

        await sender.SendAsync(new InvitationEmail("carol@example.com", "My Test Org", "Member", "http://localhost:5173/invite/abc", DateTime.UtcNow.AddDays(7), Guid.NewGuid()));

        mailbox.List().Should().ContainSingle(e => e.To == "carol@example.com" && e.AcceptUrl == "http://localhost:5173/invite/abc");
    }

    [Fact]
    public async Task Endpoint_SignedIn_ReturnsTheKeptEmails()
    {
        var to = $"dev-{Guid.NewGuid():N}@example.com";
        _factory.Services.GetRequiredService<DevMailbox>().Add(Email(to));

        var response = await SendSignedInAsync(_factory.CreateClient());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        data.EnumerateArray().Select(e => e.GetProperty("to").GetString()).Should().Contain(to);
    }

    [Fact]
    public void Endpoint_SignedOut_Unauthorized()
    {
        // The shared test host signs every request in (TestAuthHandler is its default scheme), so the
        // controller is called directly with an anonymous user
        var controller = Controller(new DevMailbox(), authenticated: false);

        controller.InvitationEmails().Result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public void Endpoint_WithoutTheMailbox_NotFound()
    {
        // What a host outside Development looks like: the tools are not registered
        var controller = Controller(mailbox: null, authenticated: true);

        controller.InvitationEmails().Result.Should().BeOfType<NotFoundResult>();
    }

    // ── Accept-URL seed ───────────────────────────────────────────

    [Fact]
    public async Task Seeder_SetsTheUrlWhenMissingOrEmpty_AndKeepsAnyOtherValue()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
        var (definitionId, levelId) = await EnsureDefinitionAndSystemLevelAsync(db);
        var seeder = new DevSettingsSeeder(db, NullLogger<DevSettingsSeeder>.Instance);

        await RemoveValueAsync(db, definitionId, levelId);
        await seeder.SeedAsync();
        (await ValueAsync(db, definitionId, levelId)).Should().Be(DevSettingsSeeder.AcceptUrl, "missing");

        await SetValueAsync(db, definitionId, levelId, "");
        await seeder.SeedAsync();
        (await ValueAsync(db, definitionId, levelId)).Should().Be(DevSettingsSeeder.AcceptUrl, "empty");

        await SetValueAsync(db, definitionId, levelId, "https://app.example.com/invite/{token}");
        await seeder.SeedAsync();
        (await ValueAsync(db, definitionId, levelId)).Should().Be("https://app.example.com/invite/{token}", "set by someone");

        await RemoveValueAsync(db, definitionId, levelId);
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static DevEmail Email(string to) =>
        new(Guid.NewGuid(), to, "My Test Org", "Member", "http://localhost:5173/invite/tok", DateTime.UtcNow.AddDays(7), DateTime.UtcNow);

    private static DevController Controller(DevMailbox? mailbox, bool authenticated)
    {
        var services = new ServiceCollection();
        if (mailbox is not null)
        {
            services.AddSingleton(mailbox);
        }

        var identity = authenticated ? new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], "Test") : new ClaimsIdentity();
        return new DevController(services.BuildServiceProvider())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } }
        };
    }
    private static Task<HttpResponseMessage> SendSignedInAsync(HttpClient client)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/dev/invitation-emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenantId: null));
        return client.SendAsync(request);
    }

    /// <summary>The setting definition and the System level, created when the host's seeders have not.</summary>
    private static async Task<(Guid DefinitionId, Guid LevelId)> EnsureDefinitionAndSystemLevelAsync(SampleDbContext db)
    {
        var level = await db.Set<SettingLevel>().Where(l => l.ParentId == null).OrderBy(l => l.Name == "System" ? 0 : 1).FirstOrDefaultAsync();
        if (level is null)
        {
            level = new SettingLevel { Name = "System", DisplayOrder = 0 };
            db.Set<SettingLevel>().Add(level);
        }

        var definition = await db.Set<SettingDefinition>().FirstOrDefaultAsync(d => d.Key == InvitationSettingKeys.AcceptUrl);
        if (definition is null)
        {
            definition = new SettingDefinition
            {
                Key = InvitationSettingKeys.AcceptUrl,
                DataType = GroundUp.Core.Enums.SettingDataType.String,
                DefaultValue = "",
                DisplayName = "Invitation Accept URL",
                IsVisible = true
            };
            db.Set<SettingDefinition>().Add(definition);
        }

        await db.SaveChangesAsync();
        return (definition.Id, level.Id);
    }

    private static Task<string?> ValueAsync(SampleDbContext db, Guid definitionId, Guid levelId)
        => db.Set<SettingValue>().AsNoTracking()
            .Where(v => v.SettingDefinitionId == definitionId && v.LevelId == levelId && v.ScopeId == null)
            .Select(v => v.Value)
            .SingleOrDefaultAsync();

    private static async Task SetValueAsync(SampleDbContext db, Guid definitionId, Guid levelId, string value)
    {
        await RemoveValueAsync(db, definitionId, levelId);
        db.Set<SettingValue>().Add(new SettingValue { SettingDefinitionId = definitionId, LevelId = levelId, Value = value });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task RemoveValueAsync(SampleDbContext db, Guid definitionId, Guid levelId)
    {
        await db.Set<SettingValue>()
            .Where(v => v.SettingDefinitionId == definitionId && v.LevelId == levelId && v.ScopeId == null)
            .ExecuteDeleteAsync();
        db.ChangeTracker.Clear();
    }
}
