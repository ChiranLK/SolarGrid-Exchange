/*
 * UserService.cs
 * -----------------------------------------------------------------------------
 * Purpose : Business logic for Backoffice user management: creating staff
 *           accounts, listing users, listing pending activations, and
 *           activating a prosumer or staff account.
 */

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

        public UserService(MongoDbContext context)
        {
            _context = context;
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

            // 4) Build the new staff user. Staff accounts start Active, not PendingActivation,
            // since Backoffice is creating and vouching for them directly.
            var now = DateTime.UtcNow;
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

        // Convenience wrapper: the Backoffice "pending activations" view is just
        // GetAllAsync with no role filter and Status = PendingActivation.
        public Task<List<UserResponseDto>> GetPendingActivationsAsync()
        {
            return GetAllAsync(null, "PendingActivation");
        }

        // Activates a user (PendingActivation -> Active, or Deactivated -> Active) and
        // clears any pending deactivation request. Deliberately does not branch on the
        // user's current status: re-activating an already-Active user is harmless, and
        // reactivating a Deactivated one is exactly the Backoffice-only action the brief
        // calls for. That restriction is enforced by the controller's [Authorize] on this
        // endpoint, not here -- make sure that attribute is present.
        public async Task<UserResponseDto> ActivateAsync(string nic)
        {
            string normalisedNic = nic.Trim().ToUpperInvariant();

            var update = Builders<User>.Update
                .Set(u => u.Status, UserStatus.Active)
                .Set(u => u.DeactivationRequested, false)
                .Set(u => u.UpdatedAtUtc, DateTime.UtcNow);

            // FindOneAndUpdateAsync applies the update and returns the resulting
            // document in one round trip, instead of a separate UpdateOneAsync + Find.
            User? updatedUser = await _context.Users.FindOneAndUpdateAsync<User>(
                u => u.Nic == normalisedNic,
                update,
                new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After });

            if (updatedUser is null)
            {
                throw new NotFoundException($"No user found with NIC '{normalisedNic}'.");
            }

            return UserMapper.ToUserResponse(updatedUser);
        }
    }
}