/*
 * UserAdministrationServiceTests.cs
 * -----------------------------------------------------------------------------
 * File        : UserAdministrationServiceTests.cs
 * Author      : H.A.S MADUWANTHA
 * IT Number   : IT23472020
 * Description : Unit tests for the Backoffice administration logic in
 *               UserService: deactivation-request listing, activate /
 *               reactivate / deactivate status transitions (valid, invalid and
 *               duplicate), Grid Operator station assignment, and safe DTOs.
 *               Uses InMemoryUserStore so no MongoDB server is required.
 * Date        : 2026-09-29
 * -----------------------------------------------------------------------------
 */

using MongoDB.Bson;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.DTOs;
using SolarMicrogrid.API.Models.DTOs.Users;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Services;
using Xunit;

namespace SolarMicrogrid.Tests;

public sealed class UserAdministrationServiceTests
{
    // Synthetic test NICs; not real people.
    internal const string AdminNic = "200000000001";
    internal const string OtherAdminNic = "200000000002";
    internal const string OperatorNic = "200000000003";
    internal const string ProsumerNic = "200000000004";
    internal const string PendingNic = "200000000005";
    internal const string DeactivatedNic = "200000000006";

    internal static readonly DateTimeOffset FixedNow = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    // ---- Deactivation-request listing -------------------------------------------------

    [Fact]
    public async Task DeactivationRequests_ReturnsOnlyFlaggedProsumersOldestFirst()
    {
        // Proves filtering (Prosumer + flagged), ordering by request time, and safe fields.
        var later = Account("200000000010", UserRole.Prosumer, UserStatus.Active);
        later.DeactivationRequested = true;
        later.DeactivationRequestedAtUtc = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
        var earlier = Account("200000000011", UserRole.Prosumer, UserStatus.Active);
        earlier.DeactivationRequested = true;
        earlier.DeactivationRequestedAtUtc = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);
        var notRequested = Account("200000000012", UserRole.Prosumer, UserStatus.Active);
        var flaggedStaff = Account("200000000013", UserRole.GridOperator, UserStatus.Active);
        flaggedStaff.DeactivationRequested = true;
        var (service, store) = Create(later, earlier, notRequested, flaggedStaff);

        List<DeactivationRequestResponseDto> result = await service.GetDeactivationRequestsAsync();

        Assert.Equal(new[] { earlier.Nic, later.Nic }, result.Select(item => item.Nic));
        Assert.Equal(earlier.DeactivationRequestedAtUtc, result[0].DeactivationRequestedAtUtc);
        Assert.Equal("Active", result[0].Status);
        Assert.Equal(new BsonDocument { { "deactivation_requested_at", 1 }, { "_id", 1 } }, store.LastSort);
    }

    [Fact]
    public async Task DeactivationRequests_EmptyWhenNonePending()
    {
        // Proves "no requests" is a normal empty list, not an error.
        var (service, _) = Create(Account(ProsumerNic, UserRole.Prosumer, UserStatus.Active));

        List<DeactivationRequestResponseDto> result = await service.GetDeactivationRequestsAsync();

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(typeof(DeactivationRequestResponseDto))]
    [InlineData(typeof(UserResponseDto))]
    public void AdministrationDtos_ExposeNoSecurityFields(Type dtoType)
    {
        // Proves the response DTOs cannot leak password hashes or other security material.
        string[] forbidden = ["Password", "Hash", "Token", "Secret"];

        Assert.DoesNotContain(dtoType.GetProperties(), property =>
            forbidden.Any(word => property.Name.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }

    // ---- Activate / reactivate --------------------------------------------------------

    [Fact]
    public async Task Activate_PendingProsumerBecomesActive()
    {
        // Proves PendingActivation -> Active and records the update time.
        var (service, store) = Create(Account(PendingNic, UserRole.Prosumer, UserStatus.PendingActivation));

        UserResponseDto result = await service.ActivateAsync(PendingNic);

        Assert.Equal("Active", result.Status);
        Assert.Equal(UserStatus.Active, store.Get(PendingNic)!.Status);
        Assert.Equal(FixedNow.UtcDateTime, store.Get(PendingNic)!.UpdatedAtUtc);
    }

    [Fact]
    public async Task Activate_DeactivatedAccountIsReactivatedAndRequestCleared()
    {
        // Proves Deactivated -> Active and that no stale deactivation request survives.
        var user = Account(DeactivatedNic, UserRole.Prosumer, UserStatus.Deactivated);
        user.DeactivationRequested = true;
        user.DeactivationRequestedAtUtc = FixedNow.UtcDateTime.AddDays(-3);
        var (service, store) = Create(user);

        UserResponseDto result = await service.ActivateAsync(DeactivatedNic.ToLowerInvariant());

        User stored = store.Get(DeactivatedNic)!;
        Assert.Equal("Active", result.Status);
        Assert.False(stored.DeactivationRequested);
        Assert.Null(stored.DeactivationRequestedAtUtc);
    }

    [Fact]
    public async Task Activate_AlreadyActiveIsConflict()
    {
        // Proves Active -> Active is rejected instead of silently succeeding.
        var (service, store) = Create(Account(ProsumerNic, UserRole.Prosumer, UserStatus.Active));

        ConflictException error = await Assert.ThrowsAsync<ConflictException>(() => service.ActivateAsync(ProsumerNic));

        Assert.Contains("already Active", error.Message);
        Assert.Equal(0, store.UpdateCount);
    }

    [Fact]
    public async Task Activate_UnknownNicIsNotFound()
    {
        // Proves activating a missing account returns 404.
        var (service, _) = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => service.ActivateAsync(ProsumerNic));
    }

    // ---- Deactivate -------------------------------------------------------------------

    [Fact]
    public async Task Deactivate_ActiveProsumerWithRequestIsFinalised()
    {
        // Proves Active -> Deactivated, the request is cleared, and the timestamp recorded.
        var prosumer = Account(ProsumerNic, UserRole.Prosumer, UserStatus.Active);
        prosumer.DeactivationRequested = true;
        prosumer.DeactivationRequestedAtUtc = FixedNow.UtcDateTime.AddDays(-1);
        var (service, store) = Create(Admin(), prosumer);

        UserResponseDto result = await service.DeactivateAsync(ProsumerNic, AdminNic);

        User stored = store.Get(ProsumerNic)!;
        Assert.Equal("Deactivated", result.Status);
        Assert.False(result.DeactivationRequested);
        Assert.Equal(UserStatus.Deactivated, stored.Status);
        Assert.Null(stored.DeactivationRequestedAtUtc);
        Assert.Equal(FixedNow.UtcDateTime, stored.UpdatedAtUtc);
        Assert.Empty(await service.GetDeactivationRequestsAsync());
    }

    [Fact]
    public async Task Deactivate_GridOperatorWithoutRequestIsAllowed()
    {
        // Proves Backoffice may deactivate any active non-self account, not only requesters.
        var (service, store) = Create(Admin(), Account(OperatorNic, UserRole.GridOperator, UserStatus.Active));

        await service.DeactivateAsync(OperatorNic, AdminNic);

        Assert.Equal(UserStatus.Deactivated, store.Get(OperatorNic)!.Status);
    }

    [Fact]
    public async Task Deactivate_AlreadyDeactivatedIsConflict()
    {
        // Proves a duplicate deactivation is rejected without writing.
        var (service, store) = Create(Admin(), Account(DeactivatedNic, UserRole.Prosumer, UserStatus.Deactivated));

        ConflictException error = await Assert.ThrowsAsync<ConflictException>(
            () => service.DeactivateAsync(DeactivatedNic, AdminNic));

        Assert.Equal("This account is already deactivated.", error.Message);
        Assert.Equal(0, store.UpdateCount);
    }

    [Fact]
    public async Task Deactivate_PendingAccountIsConflict()
    {
        // Proves PendingActivation -> Deactivated is not a valid transition.
        var (service, store) = Create(Admin(), Account(PendingNic, UserRole.Prosumer, UserStatus.PendingActivation));

        await Assert.ThrowsAsync<ConflictException>(() => service.DeactivateAsync(PendingNic, AdminNic));

        Assert.Equal(UserStatus.PendingActivation, store.Get(PendingNic)!.Status);
    }

    [Fact]
    public async Task Deactivate_SelfIsBadRequest()
    {
        // Proves a Backoffice user cannot lock themselves out, whatever NIC casing is used.
        var (service, store) = Create(Admin(), Account(OtherAdminNic, UserRole.Backoffice, UserStatus.Active));

        await Assert.ThrowsAsync<BadRequestException>(() => service.DeactivateAsync($" {AdminNic} ", AdminNic));

        Assert.Equal(UserStatus.Active, store.Get(AdminNic)!.Status);
    }

    [Fact]
    public async Task Deactivate_LastActiveBackofficeIsConflict()
    {
        // Proves the system always keeps at least one active Backoffice account.
        var deactivatedAdmin = Account(OtherAdminNic, UserRole.Backoffice, UserStatus.Deactivated);
        var target = Account("200000000020", UserRole.Backoffice, UserStatus.Active);
        var (service, store) = Create(deactivatedAdmin, target);

        ConflictException error = await Assert.ThrowsAsync<ConflictException>(
            () => service.DeactivateAsync(target.Nic, AdminNic));

        Assert.Contains("last active Backoffice", error.Message);
        Assert.Equal(UserStatus.Active, store.Get(target.Nic)!.Status);
    }

    [Fact]
    public async Task Deactivate_BackofficeAllowedWhenAnotherActiveBackofficeRemains()
    {
        // Proves the last-admin guard does not block ordinary Backoffice deactivation.
        var (service, store) = Create(Admin(), Account(OtherAdminNic, UserRole.Backoffice, UserStatus.Active));

        await service.DeactivateAsync(OtherAdminNic, AdminNic);

        Assert.Equal(UserStatus.Deactivated, store.Get(OtherAdminNic)!.Status);
    }

    [Fact]
    public async Task Deactivate_UnknownNicIsNotFound()
    {
        // Proves deactivating a missing account returns 404.
        var (service, _) = Create(Admin());

        await Assert.ThrowsAsync<NotFoundException>(() => service.DeactivateAsync(ProsumerNic, AdminNic));
    }

    [Fact]
    public async Task DeactivateThenReactivate_RoundTripsToActive()
    {
        // Proves the full Active -> Deactivated -> Active lifecycle through the service.
        var (service, store) = Create(Admin(), Account(ProsumerNic, UserRole.Prosumer, UserStatus.Active));

        await service.DeactivateAsync(ProsumerNic, AdminNic);
        await Assert.ThrowsAsync<ConflictException>(() => service.DeactivateAsync(ProsumerNic, AdminNic));
        await service.ActivateAsync(ProsumerNic);

        Assert.Equal(UserStatus.Active, store.Get(ProsumerNic)!.Status);
    }

    // ---- Station assignment -----------------------------------------------------------

    [Fact]
    public async Task AssignStation_GridOperatorGetsCanonicalStationId()
    {
        // Proves an upper-case hex ID is stored in the lower-case form other components compare against.
        SolarStationInfo station = Station(isActive: true);
        var (service, store) = Create(station, Account(OperatorNic, UserRole.GridOperator, UserStatus.Active));

        UserResponseDto result = await service.AssignStationAsync(OperatorNic, $" {station.Id.ToUpperInvariant()} ");

        Assert.Equal(station.Id, result.AssignedStationId);
        Assert.Equal(station.Id, store.Get(OperatorNic)!.AssignedStationId);
        Assert.Equal(FixedNow.UtcDateTime, store.Get(OperatorNic)!.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("not-an-object-id")]
    [InlineData("")]
    [InlineData("12345")]
    public async Task AssignStation_InvalidIdIsBadRequest(string stationId)
    {
        // Proves malformed station IDs are rejected before any database write.
        var (service, store) = Create(Station(isActive: true), Account(OperatorNic, UserRole.GridOperator, UserStatus.Active));

        await Assert.ThrowsAsync<BadRequestException>(() => service.AssignStationAsync(OperatorNic, stationId));

        Assert.Equal(0, store.UpdateCount);
    }

    [Fact]
    public async Task AssignStation_NonexistentStationIsNotFound()
    {
        // Proves a well-formed but unknown station ID cannot be assigned.
        var (service, store) = Create(station: null, Account(OperatorNic, UserRole.GridOperator, UserStatus.Active));

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.AssignStationAsync(OperatorNic, ObjectId.GenerateNewId().ToString()));

        Assert.Null(store.Get(OperatorNic)!.AssignedStationId);
    }

    [Fact]
    public async Task AssignStation_InactiveStationIsConflict()
    {
        // Proves a deactivated Member 2 station cannot receive a new operator.
        SolarStationInfo station = Station(isActive: false);
        var (service, store) = Create(station, Account(OperatorNic, UserRole.GridOperator, UserStatus.Active));

        await Assert.ThrowsAsync<ConflictException>(() => service.AssignStationAsync(OperatorNic, station.Id));

        Assert.Equal(0, store.UpdateCount);
    }

    [Theory]
    [InlineData(UserRole.Prosumer)]
    [InlineData(UserRole.Backoffice)]
    public async Task AssignStation_WrongRoleIsBadRequest(UserRole role)
    {
        // Proves only Grid Operators can hold a station assignment.
        SolarStationInfo station = Station(isActive: true);
        var (service, store) = Create(station, Account(ProsumerNic, role, UserStatus.Active));

        BadRequestException error = await Assert.ThrowsAsync<BadRequestException>(
            () => service.AssignStationAsync(ProsumerNic, station.Id));

        Assert.Equal("Only Grid Operator accounts can be assigned to a station.", error.Message);
        Assert.Null(store.Get(ProsumerNic)!.AssignedStationId);
    }

    [Fact]
    public async Task AssignStation_UnknownUserIsNotFound()
    {
        // Proves assignment to a missing account returns 404.
        SolarStationInfo station = Station(isActive: true);
        var (service, _) = Create(station);

        await Assert.ThrowsAsync<NotFoundException>(() => service.AssignStationAsync(OperatorNic, station.Id));
    }

    [Fact]
    public async Task CreateStaff_GridOperatorWithStationIsStoredAssigned()
    {
        // Proves a Grid Operator can be created and assigned in one validated call.
        SolarStationInfo station = Station(isActive: true);
        var (service, store) = Create(station);

        UserResponseDto result = await service.CreateStaffUserAsync(StaffRequest("GridOperator", station.Id));

        Assert.Equal(station.Id, result.AssignedStationId);
        Assert.Equal(station.Id, store.Get(OperatorNic)!.AssignedStationId);
    }

    [Fact]
    public async Task CreateStaff_BackofficeWithStationIsBadRequest()
    {
        // Proves station assignment is refused for the wrong role at creation time too.
        SolarStationInfo station = Station(isActive: true);
        var (service, store) = Create(station);

        await Assert.ThrowsAsync<BadRequestException>(
            () => service.CreateStaffUserAsync(StaffRequest("Backoffice", station.Id)));

        Assert.Empty(store.All);
    }

    [Fact]
    public async Task CreateStaff_GridOperatorWithUnknownStationIsNotCreated()
    {
        // Proves no account is inserted when the requested station does not exist.
        var (service, store) = Create(station: null);

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.CreateStaffUserAsync(StaffRequest("GridOperator", ObjectId.GenerateNewId().ToString())));

        Assert.Empty(store.All);
    }

    [Fact]
    public async Task CreateStaff_GridOperatorWithoutStationStillWorks()
    {
        // Proves the existing create-staff behaviour is preserved when no station is given.
        var (service, store) = Create(station: null);

        UserResponseDto result = await service.CreateStaffUserAsync(StaffRequest("GridOperator", stationId: null));

        Assert.Null(result.AssignedStationId);
        Assert.Equal(UserStatus.Active, store.Get(OperatorNic)!.Status);
    }

    // ---- Helpers ----------------------------------------------------------------------

    internal static User Account(string nic, UserRole role, UserStatus status)
    {
        // Builds a synthetic account in the given role and status.
        return new User
        {
            Nic = nic,
            FullName = $"Test {role} {nic[^2..]}",
            Email = $"user{nic}@example.com",
            Phone = "0770000000",
            PasswordHash = "$2a$12$not-a-real-hash",
            Role = role,
            Status = status,
            CreatedAtUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)
        };
    }

    internal static User Admin()
    {
        // The active Backoffice user performing administration in these tests.
        return Account(AdminNic, UserRole.Backoffice, UserStatus.Active);
    }

    private static SolarStationInfo Station(bool isActive)
    {
        // Builds a minimal Member 2 station document with a fresh ObjectId.
        return new SolarStationInfo
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = "Test Station",
            Address = "Test Address",
            IsActive = isActive
        };
    }

    private static CreateStaffUserDto StaffRequest(string role, string? stationId)
    {
        // Builds a valid staff-creation request for the operator test NIC.
        return new CreateStaffUserDto
        {
            Nic = OperatorNic,
            FullName = "New Staff",
            Email = "new.staff@example.com",
            Phone = "0771111111",
            Password = "ValidPassword1",
            Role = role,
            AssignedStationId = stationId
        };
    }

    private static (UserService Service, InMemoryUserStore Store) Create(params User[] users)
    {
        // Creates a UserService over an in-memory user store with no stations.
        return Create(station: null, users);
    }

    private static (UserService Service, InMemoryUserStore Store) Create(SolarStationInfo? station, params User[] users)
    {
        // Creates a UserService over an in-memory user store and an optional single station.
        var database = new MongoTestContext();
        var store = new InMemoryUserStore(database.Users, users);
        if (station is null)
        {
            database.ReturnStations();
        }
        else
        {
            database.ReturnStations(station);
        }

        return (new UserService(database.Context, new FixedClock(FixedNow)), store);
    }

    internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        // Returns a constant time so timestamps can be asserted exactly.
        public override DateTimeOffset GetUtcNow() => now;
    }
}
