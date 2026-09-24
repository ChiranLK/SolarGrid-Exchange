using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using SolarMicrogrid.API.Controllers;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Services;
using Xunit;

namespace SolarMicrogrid.Tests;

public sealed class DeletionServiceTests
{
    [Theory]
    [InlineData(ReservationStatus.Pending)]
    [InlineData(ReservationStatus.Approved)]
    [InlineData(ReservationStatus.Rejected)]
    [InlineData(ReservationStatus.Cancelled)]
    [InlineData(ReservationStatus.Completed)]
    public async Task SlotWithAnyReservationHistory_CannotBeDeleted(ReservationStatus status)
    {
        var database = new MongoTestContext();
        EnergyBookingSlot slot = Slot();
        database.ReturnSlots(slot);
        FilterDefinition<EnergyReservation>? reservationFilter = null;
        database.Reservations
            .Setup(collection => collection.FindAsync<EnergyReservation>(
                It.IsAny<FilterDefinition<EnergyReservation>>(),
                It.IsAny<FindOptions<EnergyReservation, EnergyReservation>>(),
                It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<EnergyReservation>,
                FindOptions<EnergyReservation, EnergyReservation>, CancellationToken>(
                (filter, _, _) => reservationFilter = filter)
            .ReturnsAsync(() => MongoTestContext.Cursor(
                [new EnergyReservation { SlotId = slot.Id, Status = status }]));

        await Assert.ThrowsAsync<ConflictException>(() =>
            new SlotService(database.Context).DeleteSlotAsync(slot.Id, CancellationToken.None));

        Assert.NotNull(reservationFilter);
        string query = reservationFilter.Render(new RenderArgs<EnergyReservation>(
            BsonSerializer.LookupSerializer<EnergyReservation>(), BsonSerializer.SerializerRegistry)).ToJson();
        Assert.Contains("slot_id", query);
        Assert.Contains(slot.Id, query);
        Assert.DoesNotContain("status", query);
        database.Slots.Verify(collection => collection.DeleteOneAsync(
            It.IsAny<FilterDefinition<EnergyBookingSlot>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnreferencedSlot_IsDeleted()
    {
        var database = new MongoTestContext();
        EnergyBookingSlot slot = Slot();
        database.ReturnSlots(slot);
        database.ReturnReservations();
        using var cancellationSource = new CancellationTokenSource();
        CancellationToken cancellationToken = cancellationSource.Token;
        database.Slots
            .Setup(collection => collection.DeleteOneAsync(
                It.IsAny<FilterDefinition<EnergyBookingSlot>>(),
                cancellationToken))
            .ReturnsAsync(Mock.Of<DeleteResult>(result => result.DeletedCount == 1));

        bool deleted = await new SlotService(database.Context)
            .DeleteSlotAsync(slot.Id, cancellationToken);

        Assert.True(deleted);
        database.Reservations.Verify(collection => collection.FindAsync<EnergyReservation>(
            It.IsAny<FilterDefinition<EnergyReservation>>(),
            It.IsAny<FindOptions<EnergyReservation, EnergyReservation>>(),
            cancellationToken), Times.Once);
        database.Slots.Verify(collection => collection.DeleteOneAsync(
            It.IsAny<FilterDefinition<EnergyBookingSlot>>(),
            cancellationToken), Times.Once);
    }

    [Fact]
    public async Task MissingSlot_DoesNotQueryOrDeleteReservations()
    {
        var database = new MongoTestContext();
        database.ReturnSlots();

        bool deleted = await new SlotService(database.Context).DeleteSlotAsync(
            ObjectId.GenerateNewId().ToString(), CancellationToken.None);

        Assert.False(deleted);
        database.Reservations.VerifyNoOtherCalls();
        database.Slots.Verify(collection => collection.DeleteOneAsync(
            It.IsAny<FilterDefinition<EnergyBookingSlot>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StationDelete_UsesDeactivationAndPreservesStationDocument()
    {
        var database = new MongoTestContext();
        SolarStationInfo station = StationServiceTests.Station();
        database.ReturnStations(station);
        database.ReturnReservations();
        database.Stations
            .Setup(collection => collection.FindOneAndUpdateAsync<SolarStationInfo>(
                It.IsAny<FilterDefinition<SolarStationInfo>>(),
                It.IsAny<UpdateDefinition<SolarStationInfo>>(),
                It.IsAny<FindOneAndUpdateOptions<SolarStationInfo, SolarStationInfo>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                station.IsActive = false;
                return station;
            });
        var controller = new StationsController(new StationService(
            database.Context, new ReservationGuardService(database.Context)));

        IActionResult result = await controller.DeleteStation(station.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.False(station.IsActive);
        database.Stations.Verify(collection => collection.DeleteOneAsync(
            It.IsAny<FilterDefinition<SolarStationInfo>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StationDelete_WithActiveReservation_IsBlocked()
    {
        var database = new MongoTestContext();
        SolarStationInfo station = StationServiceTests.Station();
        database.ReturnStations(station);
        database.ReturnReservations(new EnergyReservation
        {
            StationId = station.Id,
            Status = ReservationStatus.Pending
        });
        var controller = new StationsController(new StationService(
            database.Context, new ReservationGuardService(database.Context)));

        await Assert.ThrowsAsync<ConflictException>(() =>
            controller.DeleteStation(station.Id, CancellationToken.None));

        Assert.True(station.IsActive);
    }

    [Theory]
    [InlineData(typeof(SlotsController), nameof(SlotsController.DeleteSlot), "slots/{slotId}")]
    [InlineData(typeof(StationsController), nameof(StationsController.DeleteStation), "{stationId}")]
    public void DeleteRoutes_AreBackofficeOnly(Type controllerType, string actionName, string route)
    {
        MethodInfo action = controllerType.GetMethod(actionName)!;
        Assert.Equal(route, action.GetCustomAttribute<HttpDeleteAttribute>()?.Template);
        Assert.Equal(nameof(UserRole.Backoffice),
            action.GetCustomAttribute<AuthorizeAttribute>()?.Roles);
    }

    private static EnergyBookingSlot Slot() => new()
    {
        Id = ObjectId.GenerateNewId().ToString(),
        StationId = ObjectId.GenerateNewId().ToString(),
        TotalCapacityKwh = 20,
        AvailableCapacityKwh = 20
    };
}
