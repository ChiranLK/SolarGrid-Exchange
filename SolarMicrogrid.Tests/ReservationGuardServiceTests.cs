using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Services;
using Xunit;

namespace SolarMicrogrid.Tests;

public sealed class ReservationGuardServiceTests
{
    [Theory]
    [InlineData(ReservationStatus.Pending)]
    [InlineData(ReservationStatus.Approved)]
    public async Task ActiveReservation_PreventsStationDeactivation(ReservationStatus status)
    {
        var database = new MongoTestContext();
        SolarStationInfo station = StationServiceTests.Station();
        database.ReturnStations(station);
        database.ReturnReservations(new EnergyReservation
        {
            Id = ObjectId.GenerateNewId().ToString(),
            StationId = station.Id,
            Status = status
        });

        ConflictException exception = await Assert.ThrowsAsync<ConflictException>(() =>
            Service(database).DeactivateStationAsync(station.Id, CancellationToken.None));

        Assert.Contains("pending or approved reservations", exception.Message);
        Assert.True(station.IsActive);
        database.Stations.Verify(collection => collection.FindOneAndUpdateAsync<SolarStationInfo>(
            It.IsAny<FilterDefinition<SolarStationInfo>>(),
            It.IsAny<UpdateDefinition<SolarStationInfo>>(),
            It.IsAny<FindOneAndUpdateOptions<SolarStationInfo, SolarStationInfo>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoActiveReservations_DeactivatesStationAndUpdatesTimestamp()
    {
        var database = new MongoTestContext();
        SolarStationInfo station = StationServiceTests.Station();
        DateTime originalUpdatedAt = station.UpdatedAtUtc;
        database.ReturnStations(station);
        database.ReturnReservations();
        UpdateDefinition<SolarStationInfo>? appliedUpdate = null;
        database.Stations
            .Setup(collection => collection.FindOneAndUpdateAsync<SolarStationInfo>(
                It.IsAny<FilterDefinition<SolarStationInfo>>(),
                It.IsAny<UpdateDefinition<SolarStationInfo>>(),
                It.IsAny<FindOneAndUpdateOptions<SolarStationInfo, SolarStationInfo>>(),
                It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<SolarStationInfo>, UpdateDefinition<SolarStationInfo>,
                FindOneAndUpdateOptions<SolarStationInfo, SolarStationInfo>, CancellationToken>(
                (_, update, _, _) => appliedUpdate = update)
            .ReturnsAsync(() =>
            {
                station.IsActive = false;
                station.UpdatedAtUtc = DateTime.UtcNow;
                return station;
            });

        var result = await Service(database).DeactivateStationAsync(
            station.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result.IsActive);
        Assert.True(result.UpdatedAtUtc > originalUpdatedAt);
        Assert.NotNull(appliedUpdate);
        string updateJson = appliedUpdate.Render(new RenderArgs<SolarStationInfo>(
            BsonSerializer.LookupSerializer<SolarStationInfo>(), BsonSerializer.SerializerRegistry)).ToJson();
        Assert.Contains("is_active", updateJson);
        Assert.Contains("updated_at", updateJson);
    }

    [Fact]
    public async Task MissingStation_DoesNotQueryReservations()
    {
        var database = new MongoTestContext();
        database.ReturnStations();

        var result = await Service(database).DeactivateStationAsync(
            ObjectId.GenerateNewId().ToString(), CancellationToken.None);

        Assert.Null(result);
        database.Reservations.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GuardQuery_UsesStationIdAndOnlyPendingApprovedStatuses()
    {
        var database = new MongoTestContext();
        string stationId = ObjectId.GenerateNewId().ToString();
        FilterDefinition<EnergyReservation>? queriedFilter = null;
        database.Reservations
            .Setup(collection => collection.FindAsync<EnergyReservation>(
                It.IsAny<FilterDefinition<EnergyReservation>>(),
                It.IsAny<FindOptions<EnergyReservation, EnergyReservation>>(),
                It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<EnergyReservation>,
                FindOptions<EnergyReservation, EnergyReservation>, CancellationToken>(
                (filter, _, _) => queriedFilter = filter)
            .ReturnsAsync(() => MongoTestContext.Cursor<EnergyReservation>([]));

        bool found = await new ReservationGuardService(database.Context)
            .HasActiveReservationsForStationAsync(stationId, CancellationToken.None);

        Assert.False(found);
        Assert.NotNull(queriedFilter);
        BsonDocument filter = queriedFilter.Render(new RenderArgs<EnergyReservation>(
            BsonSerializer.LookupSerializer<EnergyReservation>(), BsonSerializer.SerializerRegistry));
        string json = filter.ToJson();
        Assert.Contains("station_id", json);
        Assert.Contains(stationId, json);
        Assert.Contains("Pending", json);
        Assert.Contains("Approved", json);
        Assert.DoesNotContain("Rejected", json);
        Assert.DoesNotContain("Cancelled", json);
        Assert.DoesNotContain("Completed", json);
    }

    private static StationService Service(MongoTestContext database) =>
        new(database.Context, new ReservationGuardService(database.Context));
}
