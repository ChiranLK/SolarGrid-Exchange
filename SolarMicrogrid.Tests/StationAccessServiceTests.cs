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
        AuthorizeAttribute? authorization = typeof(SlotsController)
            .GetMethod(methodName)!
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal(nameof(UserRole.Backoffice), authorization?.Roles);
    }

    private static StationAccessService Service(MongoTestContext database) =>
        new(database.Context, new SlotService(database.Context));

    private static MongoTestContext DatabaseWith(User user, EnergyBookingSlot slot)
    {
        var database = new MongoTestContext();
        database.ReturnUsers(user);
        database.ReturnSlots(slot);
        return database;
    }

    private static void SetupAvailabilityUpdate(
        MongoTestContext database,
        EnergyBookingSlot slot)
    {
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
