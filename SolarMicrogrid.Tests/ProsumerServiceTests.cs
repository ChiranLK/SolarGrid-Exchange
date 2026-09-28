/*
 * ProsumerServiceTests.cs
 * -----------------------------------------------------------------------------
 * File        : ProsumerServiceTests.cs
 * Author      : H.A.S MADUWANTHA
 * IT Number   : IT23472020
 * Description : Unit tests for ProsumerService: own-profile reads, profile
 *               update trimming/validation/field protection, duplicate email
 *               handling, and deactivation requests. MongoDB is mocked through
 *               MongoTestContext, so no database server is required.
 * Date        : 2026-09-29
 * -----------------------------------------------------------------------------
 */

using System.Net;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;
using Moq;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.DTOs;
using SolarMicrogrid.API.Models.DTOs.Prosumers;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Services;
using Xunit;

namespace SolarMicrogrid.Tests;

public sealed class ProsumerServiceTests
{
    internal const string CallerNic = "200012345678";
    private const string OtherNic = "199912345678";

    private static readonly DateTimeOffset FixedNow = new(2026, 9, 29, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetProfile_ReturnsOnlyTheCallersOwnDocument()
    {
        // Proves the lookup is keyed by the JWT NIC and returns public fields only.
        var fixture = new Fixture(Prosumer());

        ProsumerProfileResponseDto profile =
            await fixture.Service.GetProfileAsync(CallerNic, CancellationToken.None);

        Assert.Equal(CallerNic, profile.Nic);
        Assert.Equal("Active", profile.Status);
        Assert.Contains(fixture.UserFilters, filter => filter == $"{{ \"_id\" : \"{CallerNic}\" }}");
        Assert.DoesNotContain(
            typeof(ProsumerProfileResponseDto).GetProperties(),
            property => property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetProfile_NormalisesLowercaseNicFromToken()
    {
        // Proves an "x/v" suffix NIC matches the upper-cased stored key.
        var user = Prosumer();
        user.Nic = "991234567V";
        var fixture = new Fixture(user);

        ProsumerProfileResponseDto profile =
            await fixture.Service.GetProfileAsync(" 991234567v ", CancellationToken.None);

        Assert.Equal("991234567V", profile.Nic);
    }

    [Fact]
    public async Task GetProfile_UnknownNicIsUnauthorized()
    {
        // Proves a token for a deleted/unknown account cannot read anything.
        var fixture = new Fixture();

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => fixture.Service.GetProfileAsync(CallerNic, CancellationToken.None));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("")]
    public async Task GetProfile_BlankNicIsUnauthorized(string nic)
    {
        // Proves a token without a usable NIC claim is rejected before any database call.
        var fixture = new Fixture(Prosumer());

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => fixture.Service.GetProfileAsync(nic, CancellationToken.None));
        Assert.Empty(fixture.UserFilters);
    }

    [Theory]
    [InlineData(UserRole.GridOperator)]
    [InlineData(UserRole.Backoffice)]
    public async Task GetProfile_StaffAccountIsForbidden(UserRole role)
    {
        // Proves the prosumer profile cannot be used by staff accounts even if routing were bypassed.
        var user = Prosumer();
        user.Role = role;
        var fixture = new Fixture(user);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => fixture.Service.GetProfileAsync(CallerNic, CancellationToken.None));
    }

    [Theory]
    [InlineData(UserStatus.PendingActivation)]
    [InlineData(UserStatus.Deactivated)]
    public async Task GetProfile_InactiveAccountIsForbidden(UserStatus status)
    {
        // Proves a pending or deactivated account cannot use an unexpired token here.
        var user = Prosumer();
        user.Status = status;
        var fixture = new Fixture(user);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => fixture.Service.GetProfileAsync(CallerNic, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateProfile_TrimsNormalisesAndWritesOnlyAllowedFields()
    {
        // Proves input is trimmed, email is lower-cased, and protected fields are never in the update.
        var fixture = new Fixture(Prosumer());

        ProsumerProfileResponseDto result = await fixture.Service.UpdateProfileAsync(
            CallerNic,
            new UpdateProfileDto
            {
                FullName = "  Nimal Perera  ",
                Email = "  Nimal.New@Example.COM ",
                Phone = " 0771234567 ",
                Address = "  12 Lake Road  "
            },
            CancellationToken.None);

        Assert.Equal("Nimal Perera", result.FullName);
        Assert.Equal("nimal.new@example.com", result.Email);
        Assert.Equal("0771234567", result.Phone);
        Assert.Equal("12 Lake Road", result.Address);

        BsonDocument update = Assert.Single(fixture.Updates);
        BsonDocument set = update["$set"].AsBsonDocument;
        Assert.Equal(
            new[] { "full_name", "email", "phone", "updated_at", "address" }.OrderBy(name => name),
            set.Names.OrderBy(name => name));
        foreach (string protectedField in new[] { "_id", "role", "status", "password_hash", "deactivation_requested" })
        {
            Assert.False(set.Contains(protectedField), $"{protectedField} must not be updated.");
        }

        Assert.Equal(FixedNow.UtcDateTime, set["updated_at"].ToUniversalTime());
    }

    [Fact]
    public async Task UpdateProfile_FilterTargetsOnlyTheCallersActiveProsumerDocument()
    {
        // Proves the write is guarded by the caller's NIC, the Prosumer role and Active status.
        var fixture = new Fixture(Prosumer());

        await fixture.Service.UpdateProfileAsync(CallerNic, ValidUpdate(), CancellationToken.None);

        string filter = Assert.Single(fixture.UpdateFilters);
        Assert.Contains($"\"_id\" : \"{CallerNic}\"", filter);
        Assert.Contains("\"role\" : \"Prosumer\"", filter);
        Assert.Contains("\"status\" : \"Active\"", filter);
    }

    [Fact]
    public async Task UpdateProfile_BlankAddressIsRemoved()
    {
        // Proves an empty optional address is unset instead of stored as whitespace.
        var fixture = new Fixture(Prosumer());
        UpdateProfileDto request = ValidUpdate();
        request.Address = "   ";

        await fixture.Service.UpdateProfileAsync(CallerNic, request, CancellationToken.None);

        BsonDocument update = Assert.Single(fixture.Updates);
        Assert.True(update["$unset"].AsBsonDocument.Contains("address"));
        Assert.False(update["$set"].AsBsonDocument.Contains("address"));
    }

    [Fact]
    public async Task UpdateProfile_KeepingOwnEmailIsAllowed()
    {
        // Proves the uniqueness check excludes the caller's own document.
        var fixture = new Fixture(Prosumer());
        UpdateProfileDto request = ValidUpdate();
        request.Email = "nimal@example.com";

        await fixture.Service.UpdateProfileAsync(CallerNic, request, CancellationToken.None);

        string emailFilter = Assert.Single(fixture.UserFilters, filter => filter.Contains("\"email\""));
        Assert.Contains($"\"_id\" : {{ \"$ne\" : \"{CallerNic}\" }}", emailFilter);
    }

    [Fact]
    public async Task UpdateProfile_EmailOwnedByAnotherUserIsConflict()
    {
        // Proves a duplicate email is rejected before any write happens.
        var other = Prosumer();
        other.Nic = OtherNic;
        other.Email = "taken@example.com";
        var fixture = new Fixture(Prosumer()) { EmailOwner = other };
        UpdateProfileDto request = ValidUpdate();
        request.Email = "TAKEN@example.com";

        ConflictException error = await Assert.ThrowsAsync<ConflictException>(
            () => fixture.Service.UpdateProfileAsync(CallerNic, request, CancellationToken.None));

        Assert.Equal("A user with this email already exists.", error.Message);
        Assert.Empty(fixture.Updates);
    }

    [Fact]
    public async Task UpdateProfile_DuplicateKeyRaceIsConflict()
    {
        // Proves the unique email index failure (code 11000) maps to 409, not 500.
        var fixture = new Fixture(Prosumer()) { ThrowDuplicateKeyOnUpdate = true };

        await Assert.ThrowsAsync<ConflictException>(
            () => fixture.Service.UpdateProfileAsync(CallerNic, ValidUpdate(), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateProfile_AccountChangedDuringSaveIsConflict()
    {
        // Proves a concurrent deactivation between read and write does not report success.
        var fixture = new Fixture(Prosumer()) { UpdateReturnsNull = true };

        await Assert.ThrowsAsync<ConflictException>(
            () => fixture.Service.UpdateProfileAsync(CallerNic, ValidUpdate(), CancellationToken.None));
    }

    [Theory]
    [InlineData("   ", "nimal@example.com", "0771234567", "Full name is required.")]
    [InlineData("Nimal", "   ", "0771234567", "Email is required.")]
    [InlineData("Nimal", "nimal@example.com", "  ", "Phone is required.")]
    [InlineData("Nimal", "not-an-email", "0771234567", "Enter a valid email address.")]
    public async Task UpdateProfile_InvalidInputAfterTrimmingIsBadRequest(
        string fullName, string email, string phone, string expectedMessage)
    {
        // Proves validation runs on trimmed values and nothing reaches MongoDB.
        var fixture = new Fixture(Prosumer());

        BadRequestException error = await Assert.ThrowsAsync<BadRequestException>(
            () => fixture.Service.UpdateProfileAsync(
                CallerNic,
                new UpdateProfileDto { FullName = fullName, Email = email, Phone = phone },
                CancellationToken.None));

        Assert.Equal(expectedMessage, error.Message);
        Assert.Empty(fixture.UserFilters);
        Assert.Empty(fixture.Updates);
    }

    [Fact]
    public async Task UpdateProfile_TooLongAddressIsBadRequest()
    {
        // Proves the optional field length limit is enforced by the service too.
        var fixture = new Fixture(Prosumer());
        UpdateProfileDto request = ValidUpdate();
        request.Address = new string('a', 201);

        await Assert.ThrowsAsync<BadRequestException>(
            () => fixture.Service.UpdateProfileAsync(CallerNic, request, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateProfile_DeactivatedAccountIsForbidden()
    {
        // Proves a deactivated user with an unexpired token cannot edit the profile.
        var user = Prosumer();
        user.Status = UserStatus.Deactivated;
        var fixture = new Fixture(user);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => fixture.Service.UpdateProfileAsync(CallerNic, ValidUpdate(), CancellationToken.None));
        Assert.Empty(fixture.Updates);
    }

    [Fact]
    public async Task RequestDeactivation_FlagsTheAccountWithTimestamp()
    {
        // Proves the request only sets the flag and timestamp; the account stays Active.
        var fixture = new Fixture(Prosumer());

        ProsumerProfileResponseDto result =
            await fixture.Service.RequestDeactivationAsync(CallerNic, CancellationToken.None);

        Assert.True(result.DeactivationRequested);
        Assert.Equal(FixedNow.UtcDateTime, result.DeactivationRequestedAtUtc);
        Assert.Equal("Active", result.Status);

        BsonDocument set = Assert.Single(fixture.Updates)["$set"].AsBsonDocument;
        Assert.Equal(
            new[] { "deactivation_requested", "deactivation_requested_at", "updated_at" }.OrderBy(name => name),
            set.Names.OrderBy(name => name));
        Assert.True(set["deactivation_requested"].AsBoolean);

        string filter = Assert.Single(fixture.UpdateFilters);
        Assert.Contains($"\"_id\" : \"{CallerNic}\"", filter);
        Assert.Contains("\"deactivation_requested\" : false", filter);
    }

    [Fact]
    public async Task RequestDeactivation_AlreadyRequestedIsConflict()
    {
        // Proves a second request while one is pending is rejected without writing.
        var user = Prosumer();
        user.DeactivationRequested = true;
        var fixture = new Fixture(user);

        ConflictException error = await Assert.ThrowsAsync<ConflictException>(
            () => fixture.Service.RequestDeactivationAsync(CallerNic, CancellationToken.None));

        Assert.Contains("already pending", error.Message);
        Assert.Empty(fixture.Updates);
    }

    [Fact]
    public async Task RequestDeactivation_ConcurrentDuplicateIsConflict()
    {
        // Proves the atomic guard catches a duplicate that slipped past the first read.
        var fixture = new Fixture(Prosumer()) { UpdateReturnsNull = true };

        await Assert.ThrowsAsync<ConflictException>(
            () => fixture.Service.RequestDeactivationAsync(CallerNic, CancellationToken.None));
    }

    [Theory]
    [InlineData(UserStatus.PendingActivation)]
    [InlineData(UserStatus.Deactivated)]
    public async Task RequestDeactivation_InactiveAccountIsForbidden(UserStatus status)
    {
        // Proves only active Prosumers can request deactivation.
        var user = Prosumer();
        user.Status = status;
        var fixture = new Fixture(user);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => fixture.Service.RequestDeactivationAsync(CallerNic, CancellationToken.None));
        Assert.Empty(fixture.Updates);
    }

    internal static User Prosumer()
    {
        // Builds a synthetic active Prosumer; the NIC is test data, not a real person.
        return new User
        {
            Nic = CallerNic,
            FullName = "Nimal Perera",
            Email = "nimal@example.com",
            Phone = "0770000000",
            Address = "1 Old Road",
            PasswordHash = "$2a$12$not-a-real-hash",
            Role = UserRole.Prosumer,
            Status = UserStatus.Active,
            CreatedAtUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)
        };
    }

    private static UpdateProfileDto ValidUpdate()
    {
        // Builds a valid profile update request used as the baseline in several tests.
        return new UpdateProfileDto
        {
            FullName = "Nimal Perera",
            Email = "nimal.new@example.com",
            Phone = "0771234567",
            Address = "12 Lake Road"
        };
    }

    internal sealed class Fixture
    {
        private readonly MongoTestContext _database = new();
        private readonly User? _caller;

        public Fixture(User? caller = null)
        {
            // Routes mocked MongoDB reads by filter shape and records every update for assertions.
            _caller = caller;

            _database.Users
                .Setup(collection => collection.FindAsync<User>(
                    It.IsAny<FilterDefinition<User>>(),
                    It.IsAny<FindOptions<User, User>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((FilterDefinition<User> filter, FindOptions<User, User> _, CancellationToken _) =>
                {
                    string rendered = Render(filter);
                    UserFilters.Add(rendered);
                    return MongoTestContext.Cursor(
                        _caller is not null && rendered.Contains($"\"{_caller.Nic}\"")
                            ? new[] { _caller }
                            : []);
                });

            _database.Users
                .Setup(collection => collection.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<User>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((FilterDefinition<User> filter, CountOptions _, CancellationToken _) =>
                {
                    UserFilters.Add(Render(filter));
                    return EmailOwner is null ? 0L : 1L;
                });

            _database.Users
                .Setup(collection => collection.FindOneAndUpdateAsync<User>(
                    It.IsAny<FilterDefinition<User>>(),
                    It.IsAny<UpdateDefinition<User>>(),
                    It.IsAny<FindOneAndUpdateOptions<User, User>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((FilterDefinition<User> filter, UpdateDefinition<User> update,
                    FindOneAndUpdateOptions<User, User> _, CancellationToken _) =>
                {
                    UpdateFilters.Add(Render(filter));
                    BsonDocument renderedUpdate = Render(update);
                    Updates.Add(renderedUpdate);
                    if (ThrowDuplicateKeyOnUpdate)
                    {
                        throw DuplicateKeyException();
                    }

                    return UpdateReturnsNull || _caller is null ? null! : Apply(_caller, renderedUpdate);
                });

            Service = new ProsumerService(_database.Context, new FixedClock(FixedNow));
        }

        public ProsumerService Service { get; }

        public User? EmailOwner { get; set; }

        public bool ThrowDuplicateKeyOnUpdate { get; set; }

        public bool UpdateReturnsNull { get; set; }

        public List<string> UserFilters { get; } = [];

        public List<string> UpdateFilters { get; } = [];

        public List<BsonDocument> Updates { get; } = [];

        private static User Apply(User original, BsonDocument update)
        {
            // Simulates MongoDB applying $set/$unset so the service returns the "after" document.
            BsonDocument document = original.ToBsonDocument();
            if (update.TryGetValue("$set", out BsonValue set))
            {
                foreach (BsonElement element in set.AsBsonDocument)
                {
                    document[element.Name] = element.Value;
                }
            }

            if (update.TryGetValue("$unset", out BsonValue unset))
            {
                foreach (BsonElement element in unset.AsBsonDocument)
                {
                    document.Remove(element.Name);
                }
            }

            return BsonSerializer.Deserialize<User>(document);
        }

        private static MongoCommandException DuplicateKeyException()
        {
            // Builds the same exception type FindOneAndUpdate raises for a unique index violation.
            var connectionId = new ConnectionId(
                new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017)));
            return new MongoCommandException(
                connectionId,
                "E11000 duplicate key error",
                new BsonDocument(),
                new BsonDocument { { "ok", 0 }, { "code", 11000 } });
        }

        private static string Render(FilterDefinition<User> filter)
        {
            // Renders a filter to JSON so tests can assert which document it targets.
            return filter.Render(new RenderArgs<User>(
                BsonSerializer.LookupSerializer<User>(), BsonSerializer.SerializerRegistry)).ToJson();
        }

        private static BsonDocument Render(UpdateDefinition<User> update)
        {
            // Renders an update so tests can assert exactly which fields are written.
            return update.Render(new RenderArgs<User>(
                BsonSerializer.LookupSerializer<User>(), BsonSerializer.SerializerRegistry)).AsBsonDocument;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        // Returns a constant time so timestamps can be asserted exactly.
        public override DateTimeOffset GetUtcNow() => now;
    }
}
