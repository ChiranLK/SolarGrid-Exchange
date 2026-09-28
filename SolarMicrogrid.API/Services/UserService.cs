/*
 * UserService.cs
 * -----------------------------------------------------------------------------
 * File        : UserService.cs
 * Author      : H.A.S MADUWANTHA
 * IT Number   : IT23472020
 * Description : Business logic for Backoffice user management: creating staff
 *               accounts, listing users and pending activations, listing
 *               Prosumer deactivation requests, activating/reactivating and
 *               deactivating accounts, and assigning Grid Operators to an
 *               existing station.
 * Status      : PendingActivation -> Active   (activate)
 * rules         Deactivated       -> Active   (reactivate)
 *               Active            -> Deactivated (deactivate)
 *               Every other transition is rejected with 409 Conflict.
 * Date        : 2026-09-29
 * -----------------------------------------------------------------------------
 */

using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using SolarMicrogrid.API.Data;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Helpers;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Models.DTOs.Users;
using SolarMicrogrid.API.Models.DTOs;

namespace SolarMicrogrid.API.Services
{
    public class UserService
    {
        private readonly MongoDbContext _context;
        private readonly TimeProvider _timeProvider;

        // Keeps the original constructor used by existing callers and tests; uses the system clock.
        public UserService(MongoDbContext context)
            : this(context, TimeProvider.System)
        {
        }

        // Receives the shared MongoDB context and clock through dependency injection.
        public UserService(MongoDbContext context, TimeProvider timeProvider)
        {
            _context = context;
            _timeProvider = timeProvider;
        }

        // Creates a Backoffice or Grid Operator account. Prosumer accounts are never
        // created here; they come from AuthService.RegisterAsync (self-registration).
        public async Task<UserResponseDto> CreateStaffUserAsync(CreateStaffUserDto dto)
        {
            // 1) Normalise the same way as registration.
            string nic = dto.Nic.Trim().ToUpperInvariant();
            string email = dto.Email.Trim().ToLowerInvariant();

            // 2) Reject duplicates up front.
            bool nicExists = await _context.Users
                .Find(u => u.Nic == nic)
                .AnyAsync();
            if (nicExists)
            {
                throw new ConflictException("A user with this NIC already exists.");
            }

            bool emailExists = await _context.Users
                .Find(u => u.Email == email)
                .AnyAsync();
            if (emailExists)
            {
                throw new ConflictException("A user with this email already exists.");
            }

            // 3) Role must parse to a real UserRole and must not be Prosumer.
            // The DTO's [RegularExpression] already blocks most bad input at the edge;
            // this is the service-layer copy of the same rule.
            if (!Enum.TryParse<UserRole>(dto.Role, out UserRole role) || role == UserRole.Prosumer)
            {
                throw new BadRequestException("Role must be either Backoffice or GridOperator.");
            }

            // 3b) An optional station assignment is only meaningful for Grid Operators, and
            // must point at an existing active Member 2 station.
            string? assignedStationId = null;
            if (!string.IsNullOrWhiteSpace(dto.AssignedStationId))
            {
                if (role != UserRole.GridOperator)
                {
                    throw new BadRequestException("Only Grid Operator accounts can be assigned to a station.");
                }

                assignedStationId = await ResolveActiveStationIdAsync(dto.AssignedStationId, CancellationToken.None);
            }

            // 4) Build the new staff user. Staff accounts start Active, not PendingActivation,
            // since Backoffice is creating and vouching for them directly.
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var user = new User
            {
                Nic = nic,
                FullName = dto.FullName,
                Email = email,
                Phone = dto.Phone,
                Address = dto.Address,
                PasswordHash = PasswordHasher.Hash(dto.Password),
                Role = role,
                Status = UserStatus.Active,
                AssignedStationId = assignedStationId,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            // 5) Insert, treating a race-condition duplicate key the same as the checks above.
            try
            {
                await _context.Users.InsertOneAsync(user);
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                throw new ConflictException("A user with this NIC or email already exists.");
            }

            return UserMapper.ToUserResponse(user);
        }

        // Lists users, optionally filtered by role and/or status. Null or empty means
        // "no filter" for that field. A non-empty value that doesn't parse to a real
        // enum member (e.g. a typo like "Pendng") is rejected with 400 instead of being
        // silently ignored -- ignoring it would make a typo look like "return everyone",
        // which is a worse failure mode than a clear error.
        public async Task<List<UserResponseDto>> GetAllAsync(string? role, string? status)
        {
            var filter = Builders<User>.Filter.Empty;

            if (!string.IsNullOrWhiteSpace(role))
            {
                if (!Enum.TryParse<UserRole>(role, true, out UserRole parsedRole))
                {
                    throw new BadRequestException($"Unknown role '{role}'.");
                }

                filter &= Builders<User>.Filter.Eq(u => u.Role, parsedRole);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<UserStatus>(status, true, out UserStatus parsedStatus))
                {
                    throw new BadRequestException($"Unknown status '{status}'.");
                }

                filter &= Builders<User>.Filter.Eq(u => u.Status, parsedStatus);
            }

            List<User> users = await _context.Users.Find(filter).ToListAsync();

            return users.Select(UserMapper.ToUserResponse).ToList();
        }

        public async Task<PagedEligibleProsumerResponseDto> SearchEligibleProsumersAsync(
            EligibleProsumerListQueryDto query,
            CancellationToken cancellationToken)
        {
            // Require a meaningful bounded search and filter eligibility in MongoDB before paging.
            string search = query.Search.Trim();
            if (search.Length < 2)
            {
                throw new ArgumentException(
                    "Enter at least two characters to search for an eligible Prosumer.",
                    nameof(query.Search));
            }

            if (search.Length > 100)
            {
                throw new ArgumentException(
                    "Prosumer search cannot exceed 100 characters.",
                    nameof(query.Search));
            }

            var pattern = new BsonRegularExpression(Regex.Escape(search), "i");
            FilterDefinition<User> filter = Builders<User>.Filter.And(
                Builders<User>.Filter.Eq(user => user.Role, UserRole.Prosumer),
                Builders<User>.Filter.Eq(user => user.Status, UserStatus.Active),
                Builders<User>.Filter.Or(
                    Builders<User>.Filter.Regex(user => user.Nic, pattern),
                    Builders<User>.Filter.Regex(user => user.FullName, pattern),
                    Builders<User>.Filter.Regex(user => user.Email, pattern)));

            long totalCount = await _context.Users.CountDocumentsAsync(
                filter,
                cancellationToken: cancellationToken);
            List<User> users = await _context.Users
                .Find(filter)
                .SortBy(user => user.FullName)
                .ThenBy(user => user.Nic)
                .Skip((query.Page - 1) * query.PageSize)
                .Limit(query.PageSize)
                .ToListAsync(cancellationToken);

            return new PagedEligibleProsumerResponseDto
            {
                Items = users.Select(user => new EligibleProsumerResponseDto
                {
                    Nic = user.Nic,
                    FullName = user.FullName,
                    Email = user.Email
                }).ToList(),
                TotalCount = totalCount,
                Page = query.Page,
                PageSize = query.PageSize,
                TotalPages = totalCount == 0
                    ? 0
                    : (int)Math.Ceiling(totalCount / (double)query.PageSize)
            };
        }

        // Convenience wrapper: the Backoffice "pending activations" view is just
        // GetAllAsync with no role filter and Status = PendingActivation.
        public Task<List<UserResponseDto>> GetPendingActivationsAsync()
        {
            return GetAllAsync(null, "PendingActivation");
        }

        // Lists Prosumers who have asked for their account to be deactivated, oldest request
        // first, so Backoffice can review them in order. An empty list means none are pending.
        public async Task<List<DeactivationRequestResponseDto>> GetDeactivationRequestsAsync(
            CancellationToken cancellationToken = default)
        {
            FilterDefinition<User> filter = Builders<User>.Filter.Eq(u => u.Role, UserRole.Prosumer)
                & Builders<User>.Filter.Eq(u => u.DeactivationRequested, true);

            List<User> users = await _context.Users
                .Find(filter)
                .SortBy(u => u.DeactivationRequestedAtUtc)
                .ThenBy(u => u.Nic)
                .ToListAsync(cancellationToken);

            return users.Select(u => new DeactivationRequestResponseDto
            {
                Nic = u.Nic,
                FullName = u.FullName,
                Email = u.Email,
                Phone = u.Phone,
                Status = u.Status.ToString(),
                DeactivationRequestedAtUtc = u.DeactivationRequestedAtUtc
            }).ToList();
        }

        // Activates a user: PendingActivation -> Active (approve a new Prosumer) or
        // Deactivated -> Active (reactivate). An already-Active account is rejected with 409
        // rather than silently "succeeding". Clears any pending deactivation request.
        // Backoffice-only access is enforced by the controller's [Authorize].
        public async Task<UserResponseDto> ActivateAsync(string nic, CancellationToken cancellationToken = default)
        {
            string normalisedNic = NormaliseNic(nic);
            DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

            var update = Builders<User>.Update
                .Set(u => u.Status, UserStatus.Active)
                .Set(u => u.DeactivationRequested, false)
                .Unset(u => u.DeactivationRequestedAtUtc)
                .Set(u => u.UpdatedAtUtc, now);

            // The status condition makes the transition atomic: only activatable states match.
            FilterDefinition<User> filter = Builders<User>.Filter.Eq(u => u.Nic, normalisedNic)
                & Builders<User>.Filter.In(u => u.Status, new[] { UserStatus.PendingActivation, UserStatus.Deactivated });

            User? updatedUser = await _context.Users.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After },
                cancellationToken);

            if (updatedUser is null)
            {
                User existing = await FindUserOrThrowAsync(normalisedNic, cancellationToken);
                throw new ConflictException(
                    $"This account cannot be activated because it is already {existing.Status}.");
            }

            return UserMapper.ToUserResponse(updatedUser);
        }

        // Deactivates an Active account (Active -> Deactivated) and finalises any pending
        // deactivation request. Rejects deactivating yourself, deactivating the last active
        // Backoffice account, and any account that is not currently Active.
        public async Task<UserResponseDto> DeactivateAsync(
            string nic,
            string callerNic,
            CancellationToken cancellationToken = default)
        {
            string normalisedNic = NormaliseNic(nic);

            if (string.Equals(normalisedNic, NormaliseNic(callerNic), StringComparison.Ordinal))
            {
                throw new BadRequestException("You cannot deactivate your own account.");
            }

            User target = await FindUserOrThrowAsync(normalisedNic, cancellationToken);
            if (target.Status != UserStatus.Active)
            {
                throw new ConflictException(target.Status == UserStatus.Deactivated
                    ? "This account is already deactivated."
                    : "This account has not been activated yet, so it cannot be deactivated.");
            }

            if (target.Role == UserRole.Backoffice)
            {
                // Never leave the system without an active Backoffice who can manage accounts.
                FilterDefinition<User> otherActiveBackoffice =
                    Builders<User>.Filter.Eq(u => u.Role, UserRole.Backoffice)
                    & Builders<User>.Filter.Eq(u => u.Status, UserStatus.Active)
                    & Builders<User>.Filter.Ne(u => u.Nic, normalisedNic);
                long others = await _context.Users.CountDocumentsAsync(
                    otherActiveBackoffice,
                    new CountOptions { Limit = 1 },
                    cancellationToken);
                if (others == 0)
                {
                    throw new ConflictException("The last active Backoffice account cannot be deactivated.");
                }
            }

            DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
            var update = Builders<User>.Update
                .Set(u => u.Status, UserStatus.Deactivated)
                .Set(u => u.DeactivationRequested, false)
                .Unset(u => u.DeactivationRequestedAtUtc)
                .Set(u => u.UpdatedAtUtc, now);

            // Guarded on Active so a concurrent deactivation cannot be applied twice.
            User? updatedUser = await _context.Users.FindOneAndUpdateAsync(
                Builders<User>.Filter.Eq(u => u.Nic, normalisedNic)
                    & Builders<User>.Filter.Eq(u => u.Status, UserStatus.Active),
                update,
                new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After },
                cancellationToken);

            if (updatedUser is null)
            {
                throw new ConflictException("This account changed while saving. Please reload and try again.");
            }

            return UserMapper.ToUserResponse(updatedUser);
        }

        // Assigns a Grid Operator to an existing, active Member 2 station. Only the account's
        // AssignedStationId is written; station data itself is never modified here.
        public async Task<UserResponseDto> AssignStationAsync(
            string nic,
            string stationId,
            CancellationToken cancellationToken = default)
        {
            string normalisedNic = NormaliseNic(nic);

            User target = await FindUserOrThrowAsync(normalisedNic, cancellationToken);
            if (target.Role != UserRole.GridOperator)
            {
                throw new BadRequestException("Only Grid Operator accounts can be assigned to a station.");
            }

            string canonicalStationId = await ResolveActiveStationIdAsync(stationId, cancellationToken);

            var update = Builders<User>.Update
                .Set(u => u.AssignedStationId, canonicalStationId)
                .Set(u => u.UpdatedAtUtc, _timeProvider.GetUtcNow().UtcDateTime);

            User? updatedUser = await _context.Users.FindOneAndUpdateAsync(
                Builders<User>.Filter.Eq(u => u.Nic, normalisedNic)
                    & Builders<User>.Filter.Eq(u => u.Role, UserRole.GridOperator),
                update,
                new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After },
                cancellationToken);

            if (updatedUser is null)
            {
                throw new ConflictException("This account changed while saving. Please reload and try again.");
            }

            return UserMapper.ToUserResponse(updatedUser);
        }

        // Validates a station ID the same way StationService does (24-hex ObjectId), confirms
        // the station exists and is active, and returns the canonical lower-case ID that
        // reservation/dashboard code compares against with ordinal string equality.
        private async Task<string> ResolveActiveStationIdAsync(string? stationId, CancellationToken cancellationToken)
        {
            if (!ObjectId.TryParse(stationId?.Trim(), out ObjectId objectId))
            {
                throw new BadRequestException("Station ID is not valid.");
            }

            string canonicalId = objectId.ToString();
            SolarStationInfo? station = await _context.Stations
                .Find(item => item.Id == canonicalId)
                .FirstOrDefaultAsync(cancellationToken);

            if (station is null)
            {
                throw new NotFoundException("The specified station does not exist.");
            }

            if (!station.IsActive)
            {
                throw new ConflictException("The specified station is inactive and cannot be assigned.");
            }

            return canonicalId;
        }

        // Loads a user by normalised NIC, or throws 404 if no such account exists.
        private async Task<User> FindUserOrThrowAsync(string normalisedNic, CancellationToken cancellationToken)
        {
            User? user = await _context.Users
                .Find(u => u.Nic == normalisedNic)
                .FirstOrDefaultAsync(cancellationToken);

            return user ?? throw new NotFoundException($"No user found with NIC '{normalisedNic}'.");
        }

        // Applies the same NIC normalisation as registration (trim + upper-case V/X suffix).
        private static string NormaliseNic(string? nic)
        {
            return (nic ?? string.Empty).Trim().ToUpperInvariant();
        }
    }
}
