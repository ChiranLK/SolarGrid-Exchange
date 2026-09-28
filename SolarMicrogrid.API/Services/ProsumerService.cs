/*
 * ProsumerService.cs
 * -----------------------------------------------------------------------------
 * File        : ProsumerService.cs
 * Author      : H.A.S MADUWANTHA
 * IT Number   : IT23472020
 * Description : Business logic for a signed-in Prosumer managing their own
 *               account: reading the profile, updating the allowed profile
 *               fields, and requesting account deactivation. Every operation
 *               is keyed by the caller's NIC (taken from the JWT by the
 *               controller), so a Prosumer can only ever touch their own
 *               document. NIC, role, status and password hash are never
 *               written here.
 * Errors      : Throws the shared domain exceptions (BadRequest, Unauthorized,
 *               Forbidden, Conflict) that ExceptionMiddleware turns into the
 *               standard { status, message } response.
 * Date        : 2026-09-29
 * -----------------------------------------------------------------------------
 */

using System.ComponentModel.DataAnnotations;
using MongoDB.Driver;
using SolarMicrogrid.API.Data;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.DTOs;
using SolarMicrogrid.API.Models.DTOs.Prosumers;
using SolarMicrogrid.API.Models.Entities;

namespace SolarMicrogrid.API.Services
{
    public class ProsumerService
    {
        // Same limits as UpdateProfileDto / RegisterRequestDto, re-checked after trimming.
        private const int MaxFullNameLength = 100;
        private const int MaxEmailLength = 100;
        private const int MaxPhoneLength = 20;
        private const int MaxAddressLength = 200;

        private static readonly EmailAddressAttribute EmailValidator = new();

        private readonly MongoDbContext _context;
        private readonly TimeProvider _timeProvider;

        public ProsumerService(MongoDbContext context, TimeProvider timeProvider)
        {
            // Receive the shared MongoDB context and clock through dependency injection.
            _context = context;
            _timeProvider = timeProvider;
        }

        public async Task<ProsumerProfileResponseDto> GetProfileAsync(
            string callerNic,
            CancellationToken cancellationToken)
        {
            // Load the caller's own document and confirm it is still an active Prosumer.
            User prosumer = await LoadActiveProsumerAsync(callerNic, cancellationToken);
            return ToProfileResponse(prosumer);
        }

        public async Task<ProsumerProfileResponseDto> UpdateProfileAsync(
            string callerNic,
            UpdateProfileDto request,
            CancellationToken cancellationToken)
        {
            // Normalise and re-validate input, block duplicate emails, then update only the
            // allowed fields on the caller's own active Prosumer document.
            ArgumentNullException.ThrowIfNull(request);

            string fullName = RequireText(request.FullName, "Full name", MaxFullNameLength);
            string email = RequireText(request.Email, "Email", MaxEmailLength).ToLowerInvariant();
            string phone = RequireText(request.Phone, "Phone", MaxPhoneLength);
            string? address = OptionalText(request.Address, "Address", MaxAddressLength);

            if (!EmailValidator.IsValid(email))
            {
                throw new BadRequestException("Enter a valid email address.");
            }

            User prosumer = await LoadActiveProsumerAsync(callerNic, cancellationToken);

            // Checked up front for a clear message; the unique email index still guards the race below.
            FilterDefinition<User> emailOwnedByOthers = Builders<User>.Filter.Eq(user => user.Email, email)
                & Builders<User>.Filter.Ne(user => user.Nic, prosumer.Nic);
            long emailOwners = await _context.Users.CountDocumentsAsync(
                emailOwnedByOthers,
                new CountOptions { Limit = 1 },
                cancellationToken);
            if (emailOwners > 0)
            {
                throw new ConflictException("A user with this email already exists.");
            }

            // Only these fields are ever written, so NIC, role, status and password hash cannot change.
            UpdateDefinition<User> update = Builders<User>.Update
                .Set(user => user.FullName, fullName)
                .Set(user => user.Email, email)
                .Set(user => user.Phone, phone)
                .Set(user => user.UpdatedAtUtc, _timeProvider.GetUtcNow().UtcDateTime);
            update = address is null
                ? update.Unset(user => user.Address)
                : update.Set(user => user.Address, address);

            User? updated;
            try
            {
                updated = await _context.Users.FindOneAndUpdateAsync(
                    ActiveProsumerFilter(prosumer.Nic),
                    update,
                    new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After },
                    cancellationToken);
            }
            catch (MongoCommandException exception) when (exception.Code == 11000)
            {
                throw new ConflictException("A user with this email already exists.");
            }

            if (updated is null)
            {
                // The account changed state between the read and the write (e.g. deactivated).
                throw new ConflictException("Your account changed while saving. Please reload and try again.");
            }

            return ToProfileResponse(updated);
        }

        public async Task<ProsumerProfileResponseDto> RequestDeactivationAsync(
            string callerNic,
            CancellationToken cancellationToken)
        {
            // Atomically flag the caller's active account for Backoffice review, and reject
            // a second request while one is already pending.
            User prosumer = await LoadActiveProsumerAsync(callerNic, cancellationToken);
            if (prosumer.DeactivationRequested)
            {
                throw new ConflictException("A deactivation request is already pending for this account.");
            }

            DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
            UpdateDefinition<User> update = Builders<User>.Update
                .Set(user => user.DeactivationRequested, true)
                .Set(user => user.DeactivationRequestedAtUtc, now)
                .Set(user => user.UpdatedAtUtc, now);

            // The DeactivationRequested == false condition makes concurrent duplicate requests safe.
            FilterDefinition<User> filter = ActiveProsumerFilter(prosumer.Nic)
                & Builders<User>.Filter.Eq(user => user.DeactivationRequested, false);

            User? updated = await _context.Users.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After },
                cancellationToken);

            if (updated is null)
            {
                throw new ConflictException("A deactivation request is already pending for this account.");
            }

            return ToProfileResponse(updated);
        }

        private async Task<User> LoadActiveProsumerAsync(string callerNic, CancellationToken cancellationToken)
        {
            // Resolve the JWT's NIC to a live document; a stale or foreign token must not proceed.
            if (string.IsNullOrWhiteSpace(callerNic))
            {
                throw new UnauthorizedException("Your session is invalid. Please sign in again.");
            }

            string nic = callerNic.Trim().ToUpperInvariant();
            User? user = await _context.Users
                .Find(item => item.Nic == nic)
                .FirstOrDefaultAsync(cancellationToken);

            if (user is null)
            {
                throw new UnauthorizedException("Your session is invalid. Please sign in again.");
            }

            if (user.Role != UserRole.Prosumer)
            {
                throw new ForbiddenException("Only Prosumer accounts can use this profile.");
            }

            if (user.Status != UserStatus.Active)
            {
                throw new ForbiddenException("This account is not active. Please contact Backoffice.");
            }

            return user;
        }

        private static FilterDefinition<User> ActiveProsumerFilter(string nic)
        {
            // Match only the caller's document while it is still an active Prosumer.
            return Builders<User>.Filter.Eq(user => user.Nic, nic)
                & Builders<User>.Filter.Eq(user => user.Role, UserRole.Prosumer)
                & Builders<User>.Filter.Eq(user => user.Status, UserStatus.Active);
        }

        private static string RequireText(string? value, string fieldName, int maxLength)
        {
            // Trim a required field and reject it if it is blank or too long after trimming.
            string trimmed = value?.Trim() ?? string.Empty;
            if (trimmed.Length == 0)
            {
                throw new BadRequestException($"{fieldName} is required.");
            }

            if (trimmed.Length > maxLength)
            {
                throw new BadRequestException($"{fieldName} cannot be longer than {maxLength} characters.");
            }

            return trimmed;
        }

        private static string? OptionalText(string? value, string fieldName, int maxLength)
        {
            // Trim an optional field; a blank value means "remove it" and is stored as absent.
            string? trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return null;
            }

            if (trimmed.Length > maxLength)
            {
                throw new BadRequestException($"{fieldName} cannot be longer than {maxLength} characters.");
            }

            return trimmed;
        }

        private static ProsumerProfileResponseDto ToProfileResponse(User user)
        {
            // Copy only public profile fields; the password hash is never exposed.
            return new ProsumerProfileResponseDto
            {
                Nic = user.Nic,
                FullName = user.FullName,
                Email = user.Email,
                Phone = user.Phone,
                Address = user.Address,
                Role = user.Role.ToString(),
                Status = user.Status.ToString(),
                DeactivationRequested = user.DeactivationRequested,
                DeactivationRequestedAtUtc = user.DeactivationRequestedAtUtc,
                CreatedAtUtc = user.CreatedAtUtc,
                UpdatedAtUtc = user.UpdatedAtUtc
            };
        }
    }
}
