using MongoDB.Driver;
using SolarMicrogrid.API.Models.Entities;

namespace SolarMicrogrid.API.Data;

public sealed class MongoDbIndexInitializer(MongoDbContext dbContext) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await dbContext.EnsureIndexesAsync(cancellationToken);

        var stationIndexes = new CreateIndexModel<SolarStationInfo>[]
        {
            new(
                Builders<SolarStationInfo>.IndexKeys.Geo2DSphere(station => station.Location),
                new CreateIndexOptions { Name = "ix_stations_location_2dsphere" }),
            new(
                Builders<SolarStationInfo>.IndexKeys.Ascending(station => station.IsActive),
                new CreateIndexOptions { Name = "ix_stations_is_active" }),
            new(
                Builders<SolarStationInfo>.IndexKeys.Ascending(station => station.Name),
                new CreateIndexOptions { Name = "ix_stations_name" })
        };

        await dbContext.Stations.Indexes.CreateManyAsync(
            stationIndexes,
            cancellationToken: cancellationToken);

        var slotIndexes = new CreateIndexModel<EnergyBookingSlot>[]
        {
            new(
                Builders<EnergyBookingSlot>.IndexKeys.Ascending(slot => slot.StationId),
                new CreateIndexOptions { Name = "ix_slots_station_id" }),
            new(
                Builders<EnergyBookingSlot>.IndexKeys.Ascending(slot => slot.StartTimeUtc),
                new CreateIndexOptions { Name = "ix_slots_start_time" }),
            new(
                Builders<EnergyBookingSlot>.IndexKeys.Ascending(slot => slot.AvailabilityStatus),
                new CreateIndexOptions { Name = "ix_slots_status" }),
            new(
                Builders<EnergyBookingSlot>.IndexKeys
                    .Ascending(slot => slot.StationId)
                    .Ascending(slot => slot.StartTimeUtc),
                new CreateIndexOptions { Name = "ix_slots_station_start" }),
            new(
                Builders<EnergyBookingSlot>.IndexKeys
                    .Ascending(slot => slot.StationId)
                    .Ascending(slot => slot.StartTimeUtc)
                    .Ascending(slot => slot.EndTimeUtc),
                new CreateIndexOptions
                {
                    Name = "ux_slots_station_start_end",
                    Unique = true
                })
        };

        await dbContext.Slots.Indexes.CreateManyAsync(
            slotIndexes,
            cancellationToken: cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
