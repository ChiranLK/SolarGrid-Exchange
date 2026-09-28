/*
 * BackofficeBootstrapInitializer.cs
 * -----------------------------------------------------------------------------
 * File        : BackofficeBootstrapInitializer.cs
 * Author      : H.A.S MADUWANTHA
 * IT Number   : IT23472020
 * Description : Creates the first Backoffice account at start-up so that
 *               Backoffice can then create every other staff account through
 *               POST /api/users. Runs after MongoDbIndexInitializer, so the
 *               unique email index already exists.
 * Security    : - Off unless BootstrapBackoffice:Enabled is true.
 *               - Does nothing if ANY Backoffice account already exists, so it
 *                 is idempotent and can never add a second admin or overwrite,
 *                 reset or elevate an existing account.
 *               - Password is hashed with the shared PasswordHasher (BCrypt)
 *                 and is never logged; log lines contain no configured values.
 *               - Credentials come only from environment variables / user
 *                 secrets, never from tracked files.
 * Date        : 2026-09-29
 * -----------------------------------------------------------------------------
 */

using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using SolarMicrogrid.API.Helpers;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Settings;

namespace SolarMicrogrid.API.Data;

public enum BackofficeBootstrapOutcome
{
    Disabled,
    BackofficeAlreadyExists,
    Created,
    IdentityInUse
}

public sealed class BackofficeBootstrapInitializer : IHostedService
{
    // Same NIC rule as registration and staff creation.
    private static readonly Regex NicPattern = new(@"^([0-9]{9}[VvXx]|[0-9]{12})$", RegexOptions.CultureInvariant);

    // Stricter than self-registration because this account can administer every other one.
    internal const int MinimumPasswordLength = 12;

    private readonly MongoDbContext _context;
    private readonly BootstrapBackofficeSettings _settings;
    private readonly ILogger<BackofficeBootstrapInitializer> _logger;
    private readonly TimeProvider _timeProvider;

    public BackofficeBootstrapInitializer(
        MongoDbContext context,
        IOptions<BootstrapBackofficeSettings> options,
        ILogger<BackofficeBootstrapInitializer> logger,
        TimeProvider timeProvider)
    {
        // Receive MongoDB, the optional bootstrap settings, logging and clock through DI.
        _context = context;
        _settings = options.Value;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Run the one-off bootstrap as part of application start-up.
        await RunAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Nothing to clean up; the bootstrap is a single start-up action.
        return Task.CompletedTask;
    }

    public async Task<BackofficeBootstrapOutcome> RunAsync(CancellationToken cancellationToken)
    {
        // Create the configured Backoffice account only when enabled and no Backoffice exists yet.
        if (!_settings.Enabled)
        {
            return BackofficeBootstrapOutcome.Disabled;
        }

        // Fail fast on bad configuration; messages name the setting, never its value.
        string nic = Require(_settings.Nic, nameof(_settings.Nic)).ToUpperInvariant();
        string fullName = Require(_settings.FullName, nameof(_settings.FullName));
        string email = Require(_settings.Email, nameof(_settings.Email)).ToLowerInvariant();
        string phone = Require(_settings.Phone, nameof(_settings.Phone));
        string password = _settings.Password ?? string.Empty;

        if (!NicPattern.IsMatch(nic))
        {
            throw Invalid(nameof(_settings.Nic), "must be 9 digits followed by V or X, or 12 digits");
        }

        if (fullName.Length > 100)
        {
            throw Invalid(nameof(_settings.FullName), "cannot be longer than 100 characters");
        }

        if (email.Length > 100 || !new EmailAddressAttribute().IsValid(email))
        {
            throw Invalid(nameof(_settings.Email), "must be a valid email address of at most 100 characters");
        }

        if (phone.Length > 20)
        {
            throw Invalid(nameof(_settings.Phone), "cannot be longer than 20 characters");
        }

        if (password.Length < MinimumPasswordLength || password.Length > 100 || string.IsNullOrWhiteSpace(password))
        {
            throw Invalid(nameof(_settings.Password), $"must be between {MinimumPasswordLength} and 100 characters");
        }

        long existingBackoffice = await _context.Users.CountDocumentsAsync(
            Builders<User>.Filter.Eq(user => user.Role, UserRole.Backoffice),
            new CountOptions { Limit = 1 },
            cancellationToken);
        if (existingBackoffice > 0)
        {
            _logger.LogInformation(
                "Backoffice bootstrap skipped: a Backoffice account already exists. " +
                "BootstrapBackoffice:Enabled can now be turned off.");
            return BackofficeBootstrapOutcome.BackofficeAlreadyExists;
        }

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        var user = new User
        {
            Nic = nic,
            FullName = fullName,
            Email = email,
            Phone = phone,
            PasswordHash = PasswordHasher.Hash(password),
            Role = UserRole.Backoffice,
            Status = UserStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        try
        {
            await _context.Users.InsertOneAsync(user, cancellationToken: cancellationToken);
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // The NIC or email already belongs to a non-Backoffice account; never take it over.
            _logger.LogWarning(
                "Backoffice bootstrap skipped: the configured NIC or email is already used by another account.");
            return BackofficeBootstrapOutcome.IdentityInUse;
        }

        _logger.LogInformation(
            "Initial Backoffice account created. Disable BootstrapBackoffice:Enabled and remove the bootstrap password from the environment.");
        return BackofficeBootstrapOutcome.Created;
    }

    private static string Require(string? value, string settingName)
    {
        // Trim a required setting and fail with the setting's name (not its value) if blank.
        string trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length > 0
            ? trimmed
            : throw Invalid(settingName, "is required when BootstrapBackoffice:Enabled is true");
    }

    private static InvalidOperationException Invalid(string settingName, string rule)
    {
        // Build a configuration error that identifies the setting without echoing secrets.
        return new InvalidOperationException($"{BootstrapBackofficeSettings.SectionName}:{settingName} {rule}.");
    }
}
