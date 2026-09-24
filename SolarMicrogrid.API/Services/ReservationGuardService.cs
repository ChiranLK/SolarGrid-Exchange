using MongoDB.Driver;
using SolarMicrogrid.API.Data;
using SolarMicrogrid.API.Models.Entities;

namespace SolarMicrogrid.API.Services;

public sealed class ReservationGuardService
{
    private static readonly ReservationStatus[] ActiveStatuses =
        [ReservationStatus.Pending, ReservationStatus.Approved];

    private readonly MongoDbContext _context;

    public ReservationGuardService(MongoDbContext context)
    {
        _context = context;
    }

    public async Task<bool> HasActiveReservationsForStationAsync(
        string stationId,
        CancellationToken cancellationToken)
    {
        FilterDefinition<EnergyReservation> filter = Builders<EnergyReservation>.Filter.And(
            Builders<EnergyReservation>.Filter.Eq(reservation => reservation.StationId, stationId),
            Builders<EnergyReservation>.Filter.In(reservation => reservation.Status, ActiveStatuses));

        EnergyReservation? reservation = await _context.Reservations
            .Find(filter)
            .Limit(1)
            .FirstOrDefaultAsync(cancellationToken);

        return reservation is not null;
    }
}
