using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Moq;
using SolarMicrogrid.API.Data;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Settings;

namespace SolarMicrogrid.Tests;

internal sealed class MongoTestContext
{
    private readonly Mock<IMongoDatabase> _database = new();

    public MongoTestContext()
    {
        Client.Setup(client => client.GetDatabase("test", null)).Returns(_database.Object);
        _database
            .Setup(database => database.GetCollection<User>("users", null))
            .Returns(Users.Object);
        _database
            .Setup(database => database.GetCollection<SolarStationInfo>("stations", null))
            .Returns(Stations.Object);
        _database
            .Setup(database => database.GetCollection<EnergyBookingSlot>("slots", null))
            .Returns(Slots.Object);
        _database
            .Setup(database => database.GetCollection<EnergyReservation>("reservations", null))
            .Returns(Reservations.Object);

        Context = new MongoDbContext(
            Client.Object,
            Options.Create(new MongoSettings
            {
                DatabaseName = "test",
                UsersCollectionName = "users",
                StationsCollectionName = "stations",
                SlotsCollectionName = "slots",
                ReservationsCollectionName = "reservations"
            }));
    }

    public Mock<IMongoClient> Client { get; } = new(MockBehavior.Strict);

    public Mock<IMongoCollection<User>> Users { get; } = new();

    public Mock<IMongoCollection<SolarStationInfo>> Stations { get; } = new();

    public Mock<IMongoCollection<EnergyBookingSlot>> Slots { get; } = new();

    public Mock<IMongoCollection<EnergyReservation>> Reservations { get; } = new();

    public MongoDbContext Context { get; }

    public string? ConfiguredStationId { get; private set; }

    public void ReturnUsers(params User[] users)
    {
        Users
            .Setup(collection => collection.FindAsync<User>(
                It.IsAny<FilterDefinition<User>>(),
                It.IsAny<FindOptions<User, User>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Cursor(users));
    }

    public void ReturnStations(params SolarStationInfo[] stations)
    {
        ConfiguredStationId = stations.FirstOrDefault()?.Id;
        Stations
            .Setup(collection => collection.FindAsync<SolarStationInfo>(
                It.IsAny<FilterDefinition<SolarStationInfo>>(),
                It.IsAny<FindOptions<SolarStationInfo, SolarStationInfo>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Cursor(stations));
    }

    public void ReturnSlots(params EnergyBookingSlot[] slots)
    {
        Slots
            .Setup(collection => collection.FindAsync<EnergyBookingSlot>(
                It.IsAny<FilterDefinition<EnergyBookingSlot>>(),
                It.IsAny<FindOptions<EnergyBookingSlot, EnergyBookingSlot>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Cursor(slots));
    }

    public void ReturnReservations(params EnergyReservation[] reservations)
    {
        Reservations
            .Setup(collection => collection.FindAsync<EnergyReservation>(
                It.IsAny<FilterDefinition<EnergyReservation>>(),
                It.IsAny<FindOptions<EnergyReservation, EnergyReservation>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Cursor(reservations));
    }

    public static IAsyncCursor<T> Cursor<T>(IEnumerable<T> values)
    {
        var cursor = new Mock<IAsyncCursor<T>>();
        cursor.SetupGet(item => item.Current).Returns(values);
        cursor.SetupSequence(item => item.MoveNext(It.IsAny<CancellationToken>()))
            .Returns(true)
            .Returns(false);
        cursor.SetupSequence(item => item.MoveNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);
        return cursor.Object;
    }
}
