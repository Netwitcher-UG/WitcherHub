using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using WitcherHub.Application.Interfaces;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using WitcherHub.Infrastructure.Seeding;

namespace WitcherHub.Tests;

/// <summary>
/// The start-up break-glass that sets an administrator's password from
/// configuration.
///
/// It exists to guarantee a way in when reset email cannot be delivered. It did
/// the opposite in production: it removed the existing password, then failed to
/// set the new one because the configured value did not satisfy the password
/// policy, and left the account with no password at all — reported at the login
/// screen as AUTH-05, "the account exists but has no password stored". Every
/// subsequent deploy repeated it.
///
/// Runs against a real PostgreSQL database when one is reachable and skips when
/// it is not. Override the connection with WITCHERHUB_TEST_DB.
/// </summary>
public class BreakGlassPasswordTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=127.0.0.1;Port=5455;Database=whbreakglass;Username=postgres";

    private const string TheAdmin = "breakglass@netwitcher.test";

    private ServiceProvider? _provider;
    private AppDbContext? _db;

    private bool Available => _provider is not null;

    public async Task InitializeAsync()
    {
        var connectionString =
            Environment.GetEnvironmentVariable("WITCHERHUB_TEST_DB") ?? DefaultConnectionString;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));

        // The same relaxations the application applies. RequireLowercase is left
        // at its default of true, which is exactly the rule the configured
        // password fell foul of.
        services.AddIdentityCore<AppUser>(options =>
            {
                options.Password.RequireDigit = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequiredLength = 6;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>();
        // No token providers: the break-glass sets a password directly and never
        // issues a reset token, and wiring them in would drag data protection into
        // a test that has nothing to protect.

        var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<AppDbContext>();

        try
        {
            await db.Database.EnsureCreatedAsync();
        }
        catch
        {
            await provider.DisposeAsync();
            return;      // no database here; every test below no-ops
        }

        _provider = provider;
        _db = db;
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
    }

    private IDataSeeder BuildSeeder(string? configuredPassword, bool resetOnStartup)
    {
        var settings = new Dictionary<string, string?>
        {
            ["BootstrapAdmin:Email"] = TheAdmin,
            ["BootstrapAdmin:ResetPasswordOnStartup"] = resetOnStartup.ToString(),
            ["BootstrapAdmin:Password"] = configuredPassword
        };

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new IdentityDataSeeder(
            _provider!.GetRequiredService<RoleManager<IdentityRole<Guid>>>(),
            _provider!.GetRequiredService<UserManager<AppUser>>(),
            NullLogger<IdentityDataSeeder>.Instance,
            configuration,
            new StubEnvironment());
    }

    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "WitcherHub.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private UserManager<AppUser> Users => _provider!.GetRequiredService<UserManager<AppUser>>();

    private async Task<AppUser> GivenTheAdminExistsWithPasswordAsync(string password)
    {
        var existing = await Users.FindByEmailAsync(TheAdmin);
        if (existing is not null)
            await Users.DeleteAsync(existing);

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = TheAdmin,
            Email = TheAdmin,
            EmailConfirmed = true
        };

        var created = await Users.CreateAsync(user, password);
        Assert.True(created.Succeeded, string.Join(" | ", created.Errors.Select(e => e.Description)));

        return user;
    }

    private async Task<AppUser> ReloadAsync() =>
        (await Users.FindByEmailAsync(TheAdmin))!;

    [Fact]
    public async Task A_password_that_fails_the_policy_leaves_the_existing_one_alone()
    {
        if (!Available) return;

        await GivenTheAdminExistsWithPasswordAsync("workingpassword");

        // No lowercase letter. RequireLowercase is the one rule still at its
        // default, so this is refused — and refusing it must not cost the
        // password that already worked.
        await BuildSeeder("NETWITCHER2026!", resetOnStartup: true).SeedAsync();

        var user = await ReloadAsync();

        Assert.NotNull(user.PasswordHash);
        Assert.True(await Users.CheckPasswordAsync(user, "workingpassword"),
            "the password that worked before the break-glass ran must still work");
    }

    [Fact]
    public async Task A_password_shorter_than_the_minimum_leaves_the_existing_one_alone()
    {
        if (!Available) return;

        await GivenTheAdminExistsWithPasswordAsync("workingpassword");

        await BuildSeeder("abc", resetOnStartup: true).SeedAsync();

        var user = await ReloadAsync();

        Assert.NotNull(user.PasswordHash);
        Assert.True(await Users.CheckPasswordAsync(user, "workingpassword"));
    }

    [Fact]
    public async Task A_valid_password_replaces_the_existing_one()
    {
        if (!Available) return;

        await GivenTheAdminExistsWithPasswordAsync("oldpassword");

        await BuildSeeder("newpassword2026", resetOnStartup: true).SeedAsync();

        var user = await ReloadAsync();

        Assert.True(await Users.CheckPasswordAsync(user, "newpassword2026"));
        Assert.False(await Users.CheckPasswordAsync(user, "oldpassword"));
    }

    [Fact]
    public async Task An_account_left_without_a_password_is_repaired_by_a_valid_one()
    {
        if (!Available) return;

        // The state production was found in: the override had already removed the
        // password and failed to set a new one.
        var user = await GivenTheAdminExistsWithPasswordAsync("something");
        await Users.RemovePasswordAsync(user);

        Assert.Null((await ReloadAsync()).PasswordHash);

        await BuildSeeder("recovered2026", resetOnStartup: true).SeedAsync();

        var repaired = await ReloadAsync();

        Assert.NotNull(repaired.PasswordHash);
        Assert.True(await Users.CheckPasswordAsync(repaired, "recovered2026"));
    }

    [Fact]
    public async Task An_account_without_a_password_is_not_made_worse_by_an_invalid_one()
    {
        if (!Available) return;

        var user = await GivenTheAdminExistsWithPasswordAsync("something");
        await Users.RemovePasswordAsync(user);

        // Still broken afterwards, but not differently broken — and the log says
        // why rather than leaving it to be discovered at the login screen.
        await BuildSeeder("NOLOWERCASE1", resetOnStartup: true).SeedAsync();

        Assert.Null((await ReloadAsync()).PasswordHash);
    }

    [Fact]
    public async Task The_override_does_nothing_unless_it_is_switched_on()
    {
        if (!Available) return;

        await GivenTheAdminExistsWithPasswordAsync("untouched2026");

        await BuildSeeder("differentpassword", resetOnStartup: false).SeedAsync();

        var user = await ReloadAsync();

        Assert.True(await Users.CheckPasswordAsync(user, "untouched2026"));
    }

    [Fact]
    public async Task The_override_does_nothing_when_no_password_is_configured()
    {
        if (!Available) return;

        await GivenTheAdminExistsWithPasswordAsync("untouched2026");

        await BuildSeeder(configuredPassword: null, resetOnStartup: true).SeedAsync();

        var user = await ReloadAsync();

        Assert.NotNull(user.PasswordHash);
        Assert.True(await Users.CheckPasswordAsync(user, "untouched2026"));
    }
}
