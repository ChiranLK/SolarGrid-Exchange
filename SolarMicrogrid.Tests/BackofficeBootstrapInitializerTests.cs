/*
 * BackofficeBootstrapInitializerTests.cs
 * -----------------------------------------------------------------------------
 * File        : BackofficeBootstrapInitializerTests.cs
 * Author      : H.A.S MADUWANTHA
 * IT Number   : IT23472020
 * Description : Tests for the initial Backoffice bootstrap: disabled by default,
 *               creates one hashed Active Backoffice when enabled, idempotent
 *               when a Backoffice already exists, never takes over an existing
 *               account, and rejects invalid configuration without echoing
 *               the configured values.
 * Date        : 2026-09-29
 * -----------------------------------------------------------------------------
 */

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SolarMicrogrid.API.Data;
using SolarMicrogrid.API.Helpers;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Settings;
using Xunit;

namespace SolarMicrogrid.Tests;

public sealed class BackofficeBootstrapInitializerTests
{
    // Test-only placeholder values; not real credentials.
    private const string BootstrapNic = "200000000099";
    private const string BootstrapPassword = "Test-Only-Bootstrap-Pass-1";

    [Fact]
    public void Settings_AreDisabledByDefault()
    {
        // Proves an unconfigured environment never creates an account.
        Assert.False(new BootstrapBackofficeSettings().Enabled);
    }

    [Fact]
    public async Task Disabled_DoesNothingEvenWithCompleteSettings()
    {
        // Proves Enabled=false is authoritative even when every other value is present.
        BootstrapBackofficeSettings settings = ValidSettings();
        settings.Enabled = false;
        var (initializer, store) = Create(settings);

        BackofficeBootstrapOutcome outcome = await initializer.RunAsync(CancellationToken.None);

        Assert.Equal(BackofficeBootstrapOutcome.Disabled, outcome);
        Assert.Empty(store.All);
    }

    [Fact]
    public async Task Disabled_DoesNotValidateMissingSettings()
    {
        // Proves a normal deployment with no bootstrap variables starts cleanly.
        var (initializer, _) = Create(new BootstrapBackofficeSettings());

        Assert.Equal(BackofficeBootstrapOutcome.Disabled, await initializer.RunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Enabled_CreatesOneActiveBackofficeWithHashedPassword()
    {
        // Proves the created account is Backoffice, Active, normalised, and stores only a BCrypt hash.
        var (initializer, store) = Create(ValidSettings());

        BackofficeBootstrapOutcome outcome = await initializer.RunAsync(CancellationToken.None);

        User created = Assert.Single(store.All);
        Assert.Equal(BackofficeBootstrapOutcome.Created, outcome);
        Assert.Equal(UserRole.Backoffice, created.Role);
        Assert.Equal(UserStatus.Active, created.Status);
        Assert.Equal("200000000099", created.Nic);
        Assert.Equal("bootstrap.admin@example.com", created.Email);
        Assert.NotEqual(BootstrapPassword, created.PasswordHash);
        Assert.True(PasswordHasher.Verify(BootstrapPassword, created.PasswordHash));
        Assert.Equal(UserAdministrationServiceTests.FixedNow.UtcDateTime, created.CreatedAtUtc);
    }

    [Fact]
    public async Task Enabled_RunningTwiceIsIdempotent()
    {
        // Proves a restart with the bootstrap still enabled does not create a second admin.
        var (initializer, store) = Create(ValidSettings());

        await initializer.RunAsync(CancellationToken.None);
        BackofficeBootstrapOutcome second = await initializer.RunAsync(CancellationToken.None);

        Assert.Equal(BackofficeBootstrapOutcome.BackofficeAlreadyExists, second);
        Assert.Single(store.All);
    }

    [Theory]
    [InlineData(UserStatus.Active)]
    [InlineData(UserStatus.Deactivated)]
    public async Task Enabled_SkipsWhenAnyBackofficeAlreadyExists(UserStatus existingStatus)
    {
        // Proves an existing Backoffice (even deactivated) is never duplicated, changed or reset.
        User existing = UserAdministrationServiceTests.Account(
            UserAdministrationServiceTests.AdminNic, UserRole.Backoffice, existingStatus);
        var (initializer, store) = Create(ValidSettings(), existing);

        BackofficeBootstrapOutcome outcome = await initializer.RunAsync(CancellationToken.None);

        Assert.Equal(BackofficeBootstrapOutcome.BackofficeAlreadyExists, outcome);
        User stored = Assert.Single(store.All);
        Assert.Equal(existingStatus, stored.Status);
        Assert.Equal(existing.PasswordHash, stored.PasswordHash);
    }

    [Fact]
    public async Task Enabled_DoesNotTakeOverAnExistingNonBackofficeAccount()
    {
        // Proves a configured email that belongs to a Prosumer is not elevated to Backoffice.
        User prosumer = UserAdministrationServiceTests.Account(
            UserAdministrationServiceTests.ProsumerNic, UserRole.Prosumer, UserStatus.Active);
        prosumer.Email = "bootstrap.admin@example.com";
        var (initializer, store) = Create(ValidSettings(), prosumer);

        BackofficeBootstrapOutcome outcome = await initializer.RunAsync(CancellationToken.None);

        Assert.Equal(BackofficeBootstrapOutcome.IdentityInUse, outcome);
        User stored = Assert.Single(store.All);
        Assert.Equal(UserRole.Prosumer, stored.Role);
    }

    [Theory]
    [InlineData(nameof(BootstrapBackofficeSettings.Nic), "not-a-nic")]
    [InlineData(nameof(BootstrapBackofficeSettings.Nic), "")]
    [InlineData(nameof(BootstrapBackofficeSettings.FullName), "  ")]
    [InlineData(nameof(BootstrapBackofficeSettings.Email), "not-an-email")]
    [InlineData(nameof(BootstrapBackofficeSettings.Phone), "")]
    [InlineData(nameof(BootstrapBackofficeSettings.Password), "Short-Pass1")]
    [InlineData(nameof(BootstrapBackofficeSettings.Password), "")]
    public async Task Enabled_InvalidSettingFailsWithoutEchoingValues(string settingName, string badValue)
    {
        // Proves misconfiguration stops start-up, names the setting, and never prints the value.
        BootstrapBackofficeSettings settings = ValidSettings();
        typeof(BootstrapBackofficeSettings).GetProperty(settingName)!.SetValue(settings, badValue);
        var (initializer, store) = Create(settings);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => initializer.RunAsync(CancellationToken.None));

        Assert.Contains($"BootstrapBackoffice:{settingName}", error.Message);
        if (badValue.Trim().Length > 0)
        {
            Assert.DoesNotContain(badValue, error.Message);
        }

        Assert.DoesNotContain(BootstrapPassword, error.Message);
        Assert.Empty(store.All);
    }

    [Fact]
    public async Task StartAsync_RunsTheBootstrap()
    {
        // Proves the hosted-service entry point performs the same work as RunAsync.
        var (initializer, store) = Create(ValidSettings());

        await initializer.StartAsync(CancellationToken.None);

        Assert.Single(store.All);
    }

    private static BootstrapBackofficeSettings ValidSettings()
    {
        // Builds a complete, valid, enabled bootstrap configuration using placeholder values.
        return new BootstrapBackofficeSettings
        {
            Enabled = true,
            Nic = BootstrapNic,
            FullName = "Bootstrap Admin",
            Email = "  Bootstrap.Admin@Example.com ",
            Phone = "0770000099",
            Password = BootstrapPassword
        };
    }

    private static (BackofficeBootstrapInitializer Initializer, InMemoryUserStore Store) Create(
        BootstrapBackofficeSettings settings,
        params User[] users)
    {
        // Builds the initializer over an in-memory user store with a fixed clock.
        var database = new MongoTestContext();
        var store = new InMemoryUserStore(database.Users, users);
        var initializer = new BackofficeBootstrapInitializer(
            database.Context,
            Options.Create(settings),
            NullLogger<BackofficeBootstrapInitializer>.Instance,
            new UserAdministrationServiceTests.FixedClock(UserAdministrationServiceTests.FixedNow));
        return (initializer, store);
    }
}
