/*
 * MongoDbContext.cs
 * -----------------------------------------------------------------------------
 * Purpose : Exposes the application's MongoDB collections through one shared
 *           context, including the Component 3 energy-reservation collection.
 * Indexes : Creates the user indexes retained from the existing implementation.
 * -----------------------------------------------------------------------------
 */

using Microsoft.Extensions.Options;
using MongoDB.Driver;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Settings;

namespace SolarMicrogrid.API.Data;

public sealed class MongoDbContext
{
    public MongoDbContext(IMongoClient mongoClient, IOptions<MongoSettings> options)
    {
        // Resolve validated settings once and bind every entity to its configured collection.
        ArgumentNullException.ThrowIfNull(mongoClient);
        ArgumentNullException.ThrowIfNull(options);

        MongoSettings settings = options.Value;
        IMongoDatabase database = mongoClient.GetDatabase(settings.DatabaseName);

        Users = database.GetCollection<User>(settings.UsersCollectionName);
        Stations = database.GetCollection<SolarStationInfo>(settings.StationsCollectionName);
        Slots = database.GetCollection<EnergyBookingSlot>(settings.SlotsCollectionName);
        Reservations = database.GetCollection<EnergyReservation>(settings.ReservationsCollectionName);
    }

    public IMongoCollection<User> Users { get; }

    public IMongoCollection<SolarStationInfo> Stations { get; }

    public IMongoCollection<EnergyBookingSlot> Slots { get; }

    public IMongoCollection<EnergyReservation> Reservations { get; }

    public async Task EnsureIndexesAsync(CancellationToken cancellationToken)
    {
        // Preserve the case-insensitive unique email constraint for user authentication.
        var emailIndex = new CreateIndexModel<User>(
            Builders<User>.IndexKeys.Ascending(user => user.Email),
            new CreateIndexOptions
            {
                Name = "ux_users_email",
                Unique = true,
                Collation = new Collation("en", strength: CollationStrength.Secondary)
            });

        await Users.Indexes.CreateOneAsync(emailIndex, cancellationToken: cancellationToken);
    }
}
