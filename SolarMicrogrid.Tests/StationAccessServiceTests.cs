/*
 * StationAccessServiceTests.cs
 * -----------------------------------------------------------------------------
 * Purpose : Verifies role, account-status and station scope for slot availability changes.
 * -----------------------------------------------------------------------------
 */

using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using SolarMicrogrid.API.Controllers;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.DTOs.Slots;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Services;
using Xunit;

namespace SolarMicrogrid.Tests;

public sealed class StationAccessServiceTests
{
    [Fact]
    public async Task Backoffice_CanChangeAvailabilityWithoutStationAssignment()
    {
        // Exercise Backoffice_CanChangeAvailabilityWithoutStationAssignment with isolated synthetic data and explicit assertions.
        EnergyBookingSlot slot = Slot();
        MongoTestContext database = DatabaseWith(
            User(UserRole.Backoffice),
            slot);
        SetupAvailabilityUpdate(database, slot);

        SlotResponseDto result = await Service(database).ChangeSlotAvailabilityAsync(
            " 200012345678 ",
            slot.Id,
            new ChangeSlotAvailabilityRequestDto { Status = "Unavailable" },
            CancellationToken.None);

        Assert.Equal("Unavailable", result.AvailabilityStatus);
    }

    [Fact]
    public async Task GridOperator_CanChangeAvailabilityForAssignedStation()
    {
        // Exercise GridOperator_CanChangeAvailabilityForAssignedStation with isolated synthetic data and explicit assertions.
        EnergyBookingSlot slot = Slot();
        MongoTestContext database = DatabaseWith(
            User(UserRole.GridOperator, slot.StationId),
            slot);
        SetupAvailabilityUpdate(database, slot);

        SlotResponseDto result = await Service(database).ChangeSlotAvailabilityAsync(
            "200012345678",
            slot.Id,
            new ChangeSlotAvailabilityRequestDto { Status = "Unavailable" },
            CancellationToken.None);

        Assert.Equal(slot.StationId, result.StationId);
        Assert.Equal("Unavailable", result.AvailabilityStatus);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("000000000000000000000000")]
    public async Task GridOperator_WithoutMatchingAssignment_IsForbidden(string? assignedStationId)
    {
        // Exercise GridOperator_WithoutMatchingAssignment_IsForbidden with isolated synthetic data and explicit assertions.
        EnergyBookingSlot slot = Slot();
        MongoTestContext database = DatabaseWith(
            User(UserRole.GridOperator, assignedStationId),
            slot);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service(database).ChangeSlotAvailabilityAsync(
                "200012345678",
                slot.Id,
                new ChangeSlotAvailabilityRequestDto { Status = "Unavailable" },
                CancellationToken.None));
    }

    [Fact]
    public async Task MissingAccount_IsUnauthorized()
    {
        // Exercise MissingAccount_IsUnauthorized with isolated synthetic data and explicit assertions.
        var database = new MongoTestContext();
        database.ReturnUsers();

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            Service(database).ChangeSlotAvailabilityAsync(
                "200012345678",
                ObjectId.GenerateNewId().ToString(),
                new ChangeSlotAvailabilityRequestDto { Status = "Unavailable" },
                CancellationToken.None));
    }

    [Fact]
    public async Task InactiveGridOperator_IsForbidden()
    {
        // Exercise InactiveGridOperator_IsForbidden with isolated synthetic data and explicit assertions.
        EnergyBookingSlot slot = Slot();
        User user = User(UserRole.GridOperator, slot.StationId);
        user.Status = UserStatus.Deactivated;
        MongoTestContext database = DatabaseWith(user, slot);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service(database).ChangeSlotAvailabilityAsync(
                user.Nic,
                slot.Id,
                new ChangeSlotAvailabilityRequestDto { Status = "Unavailable" },
                CancellationToken.None));
    }

    [Fact]
    public async Task Prosumer_IsForbiddenByBusinessService()
    {
        // Exercise Prosumer_IsForbiddenByBusinessService with isolated synthetic data and explicit assertions.
        EnergyBookingSlot slot = Slot();
        MongoTestContext database = DatabaseWith(User(UserRole.Prosumer), slot);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service(database).ChangeSlotAvailabilityAsync(
                "200012345678",
                slot.Id,
                new ChangeSlotAvailabilityRequestDto { Status = "Unavailable" },
                CancellationToken.None));
    }

    [Fact]
    public async Task MissingSlot_IsNotFound()
    {
        // Exercise MissingSlot_IsNotFound with isolated synthetic data and explicit assertions.
        var database = new MongoTestContext();
        database.ReturnUsers(User(UserRole.Backoffice));
        database.ReturnSlots();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service(database).ChangeSlotAvailabilityAsync(
                "200012345678",
                ObjectId.GenerateNewId().ToString(),
                new ChangeSlotAvailabilityRequestDto { Status = "Unavailable" },
                CancellationToken.None));
    }

    [Fact]
    public void AvailabilityEndpoint_AllowsOnlyBackofficeAndGridOperatorRoles()
    {
        // Exercise AvailabilityEndpoint_AllowsOnlyBackofficeAndGridOperatorRoles with isolated synthetic data and explicit assertions.
        AuthorizeAttribute? authorization = typeof(SlotsController)
            .GetMethod(nameof(SlotsController.ChangeSlotAvailability))!
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorization);
        string[] roles = authorization.Roles!.Split(',');
        Assert.Contains(nameof(UserRole.Backoffice), roles);
        Assert.Contains(nameof(UserRole.GridOperator), roles);
        Assert.DoesNotContain(nameof(UserRole.Prosumer), roles);
    }

    [Theory]
    [InlineData(nameof(SlotsController.CreateSlot))]
    [InlineData(nameof(SlotsController.UpdateSlot))]
    public void SlotDetailMutations_RemainBackofficeOnly(string methodName)
    {
        // Exercise SlotDetailMutations_RemainBackofficeOnly with isolated synthetic data and explicit assertions.
        AuthorizeAttribute? authorization = typeof(SlotsController)
            .GetMethod(methodName)!
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal(nameof(UserRole.Backoffice), authorization?.Roles);
    }

    private static StationAccessService Service(MongoTestContext database) =>
        new(database.Context, new SlotService(database.Context));

    private static MongoTestContext DatabaseWith(User user, EnergyBookingSlot slot)
    {
        // Exercise DatabaseWith with isolated synthetic data and explicit assertions.
        var database = new MongoTestContext();
        database.ReturnUsers(user);
        database.ReturnSlots(slot);
        return database;
    }

    private static void SetupAvailabilityUpdate(
        MongoTestContext database,
        EnergyBookingSlot slot)
    {
        // Exercise SetupAvailabilityUpdate with isolated synthetic data and explicit assertions.
        EnergyBookingSlot updatedSlot = Slot(
            slot.Id,
            slot.StationId,
            SlotAvailabilityStatus.Unavailable);
        database.Slots
            .Setup(collection => collection.FindOneAndUpdateAsync<EnergyBookingSlot>(
                It.IsAny<FilterDefinition<EnergyBookingSlot>>(),
                It.IsAny<UpdateDefinition<EnergyBookingSlot>>(),
                It.IsAny<FindOneAndUpdateOptions<EnergyBookingSlot, EnergyBookingSlot>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedSlot);
    }

    private static User User(UserRole role, string? assignedStationId = null) => new()
    {
        // Exercise User with isolated synthetic data and explicit assertions.
        Nic = "200012345678",
        FullName = "Test User",
        Email = "test@example.com",
        Phone = "0700000000",
        PasswordHash = "not-used",
        Role = role,
        Status = UserStatus.Active,
        AssignedStationId = assignedStationId,
        CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
        UpdatedAtUtc = DateTime.UtcNow.AddDays(-1)
    };

    private static EnergyBookingSlot Slot(
        string? id = null,
        string? stationId = null,
        SlotAvailabilityStatus status = SlotAvailabilityStatus.Available) => new()
    {
        // Exercise Slot with isolated synthetic data and explicit assertions.
        Id = id ?? ObjectId.GenerateNewId().ToString(),
        StationId = stationId ?? ObjectId.GenerateNewId().ToString(),
        StartTimeUtc = DateTime.UtcNow.AddHours(2),
        EndTimeUtc = DateTime.UtcNow.AddHours(3),
        TotalCapacityKwh = 20,
        AvailableCapacityKwh = 20,
        AvailabilityStatus = status,
        CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
        UpdatedAtUtc = DateTime.UtcNow.AddDays(-1)
    };
}
