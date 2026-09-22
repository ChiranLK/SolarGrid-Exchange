/*
 * AuthService.cs
 * -----------------------------------------------------------------------------
 * Purpose : Business logic for registration and login, for all roles. Equivalent
 *           to a Spring AuthService.
 * Depends : MongoDbContext (for the Users collection) and JwtHelper, both
 *           injected. PasswordHasher is static, so it is called directly.
 * Register: builder.Services.AddScoped<AuthService>(); in Program.cs.
 * Notes   : - RegisterAsync only ever creates Prosumer accounts. Backoffice and
 *             Grid Operator (staff) accounts are created by UserService, which
 *             only Backoffice can call.
 *           - The very first Backoffice user has to exist before anyone can use
 *             UserService to create more staff. Either insert one document
 *             directly in Atlas, or add a startup seed (e.g. a hosted service,
 *             like MongoDbIndexInitializer) that inserts a fixed Backoffice
 *             user the first time the app runs if the Users collection is empty.
 *           - Errors are thrown as custom exceptions (ConflictException,
 *             UnauthorizedException, ForbiddenException) and turned into HTTP
 *             status codes by ExceptionMiddleware, the same way Spring's
 *             GlobalExceptionHandler turns exceptions into responses.
 * -----------------------------------------------------------------------------
 */

using MongoDB.Driver;
using SolarMicrogrid.API.Data;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Helpers;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Models.DTOs;

namespace SolarMicrogrid.API.Services
{
    public class AuthService
    {
        private readonly MongoDbContext _context;
        private readonly JwtHelper _jwtHelper;

        public AuthService(MongoDbContext context, JwtHelper jwtHelper)
        {
            _context = context;
            _jwtHelper = jwtHelper;
        }

       
        public async Task<UserResponseDto> RegisterAsync(RegisterRequestDto request)
        {
            
            string nic = request.Nic.ToUpperInvariant();
            string email = request.Email.ToLowerInvariant();

        
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

            // 4) Build the new prosumer.
            var now = DateTime.UtcNow;
            var user = new User
            {
                Nic = nic,
                FullName = request.FullName,
                Email = email,
                Phone = request.Phone,
                Address = request.Address,
                PasswordHash = PasswordHasher.Hash(request.Password),
                Role = UserRole.Prosumer,
                Status = UserStatus.PendingActivation,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            // 4) Insert. A duplicate-key error here (race between the check above and the
            // insert) is still reported as a conflict rather than a generic 500.
            try
            {
                await _context.Users.InsertOneAsync(user);
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                throw new ConflictException("A user with this NIC or email already exists.");
            }

            // 5) Never return the password hash.
            return MapToUserResponse(user);
        }

        // Verifies credentials, checks the account is allowed to log in, updates the
        // last-login time, and returns a signed JWT with the user's basic details.
        public async Task<LoginResponseDto> LoginAsync(LoginRequestDto request)
        {
            // 1) Same normalisation as registration, so login matches regardless of case.
            string email = request.Email.ToLowerInvariant();

            User? user = await _context.Users
                .Find(u => u.Email == email)
                .FirstOrDefaultAsync();

            // 2) Not found, or password doesn't match: same message either way, so a
            // caller can't tell which part was wrong.
            if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
            {
                throw new UnauthorizedException("Invalid email or password.");
            }

            // 3) Credentials are correct, but the account may not be allowed to log in yet.
            if (user.Status == UserStatus.PendingActivation)
            {
                throw new ForbiddenException("This account is awaiting activation.");
            }

            if (user.Status == UserStatus.Deactivated)
            {
                throw new ForbiddenException("This account is deactivated. Please contact Backoffice.");
            }

            // 4) Record the successful login.
            var update = Builders<User>.Update.Set(u => u.LastLoginAtUtc, DateTime.UtcNow);
            await _context.Users.UpdateOneAsync(u => u.Nic == user.Nic, update);

            // 5) Issue the token and return the response the client needs.
            string token = _jwtHelper.GenerateToken(user);

            return new LoginResponseDto
            {
                Token = token,
                Nic = user.Nic,
                FullName = user.FullName,
                Role = user.Role.ToString(),
                Status = user.Status.ToString()
            };
        }

        // Converts a User document into the safe, public-facing DTO (no password hash).
        private static UserResponseDto MapToUserResponse(User user)
        {
            return new UserResponseDto
            {
                Nic = user.Nic,
                FullName = user.FullName,
                Email = user.Email,
                Phone = user.Phone,
                Address = user.Address,
                Role = user.Role.ToString(),
                Status = user.Status.ToString(),
                CreatedAtUtc = user.CreatedAtUtc
            };
        }
    }
}