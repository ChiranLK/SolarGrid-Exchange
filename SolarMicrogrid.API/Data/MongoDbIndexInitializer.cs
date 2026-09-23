/*
 * MongoDbIndexInitializer.cs
 * -----------------------------------------------------------------------------
 * Purpose : Creates MongoDB indexes for users, stations, slots, and Component 3
 *           reservations when the API starts.
 * Safety  : Reservation indexes support real list/ownership queries, capacity
 *           integration, idempotent creation, and active duplicate prevention.
 * -----------------------------------------------------------------------------
 */

using MongoDB.Driver;
using SolarMicrogrid.API.Models.Entities;

namespace SolarMicrogrid.API.Data;

public sealed class MongoDbIndexInitializer(MongoDbContext dbContext) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Create all declared indexes before accepting application traffic.
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
                }),
            new(
                Builders<EnergyBookingSlot>.IndexKeys.Ascending(
                    "capacity_allocations.reservation_id"),
                new CreateIndexOptions { Name = "ix_slots_capacity_allocation_reservation" })
        };

        await dbContext.Slots.Indexes.CreateManyAsync(
            slotIndexes,
            cancellationToken: cancellationToken);

        FilterDefinition<EnergyReservation> activeReservationFilter =
            Builders<EnergyReservation>.Filter.In(
                reservation => reservation.Status,
                [ReservationStatus.Pending, ReservationStatus.Approved]);

        var reservationIndexes = new CreateIndexModel<EnergyReservation>[]
        {
            new(
                Builders<EnergyReservation>.IndexKeys
                    .Ascending(reservation => reservation.ProsumerNic)
                    .Ascending(reservation => reservation.ScheduledStartTimeUtc),
                new CreateIndexOptions { Name = "ix_reservations_prosumer_start" }),
            new(
                Builders<EnergyReservation>.IndexKeys
                    .Ascending(reservation => reservation.SlotId)
                    .Ascending(reservation => reservation.Status),
                new CreateIndexOptions { Name = "ix_reservations_slot_status" }),
            new(
                Builders<EnergyReservation>.IndexKeys
                    .Ascending(reservation => reservation.StationId)
                    .Ascending(reservation => reservation.Status)
                    .Ascending(reservation => reservation.ScheduledStartTimeUtc),
                new CreateIndexOptions { Name = "ix_reservations_station_status_start" }),
            new(
                Builders<EnergyReservation>.IndexKeys
                    .Ascending(reservation => reservation.Status)
                    .Ascending(reservation => reservation.ScheduledStartTimeUtc),
                new CreateIndexOptions { Name = "ix_reservations_status_start" }),
            new(
                Builders<EnergyReservation>.IndexKeys.Ascending(
                    reservation => reservation.CreationRequestIdHash),
                new CreateIndexOptions
                {
                    Name = "ux_reservations_creation_request_id_hash",
                    Unique = true,
                    Sparse = true
                }),
            new(
                Builders<EnergyReservation>.IndexKeys
                    .Ascending(reservation => reservation.ProsumerNic)
                    .Ascending(reservation => reservation.SlotId),
                new CreateIndexOptions<EnergyReservation>
                {
                    Name = "ux_reservations_active_prosumer_slot",
                    Unique = true,
                    PartialFilterExpression = activeReservationFilter
                })
        };

        await dbContext.Reservations.Indexes.CreateManyAsync(
            reservationIndexes,
            cancellationToken: cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Index initialization owns no resources that require shutdown work.
        return Task.CompletedTask;
    }
}
