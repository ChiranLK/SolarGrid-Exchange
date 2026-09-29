/*
 * CapacityStatusPreservationTests.cs
 * -----------------------------------------------------------------------------
 * Purpose : Component 3 capacity release/adjust must not overwrite a concurrent
 *           Member 2 availability change (e.g. Unavailable). The compare-and-swap
 *           filters therefore include the availability status that the new status
 *           was derived from, so a mismatch retries with a fresh read.
 * -----------------------------------------------------------------------------
 */

using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Services;
using Xunit;

namespace SolarMicrogrid.Tests;

public sealed class CapacityStatusPreservationTests
{
    private static readonly string ReservationId = ObjectId.GenerateNewId().ToString();

    [Fact]
    public async Task Release_CompareAndSwapIncludesTheReadAvailabilityStatus()
    {
        // A slot read as FullyBooked must only be updated while it is still FullyBooked.
        var database = new MongoTestContext();
        EnergyBookingSlot slot = SlotWithClaim(SlotAvailabilityStatus.FullyBooked, available: 0m);
        database.ReturnSlots(slot);
        BsonDocument filter = CaptureSlotUpdateFilter(database, slot);

        await new ReservationCapacityService(database.Context, TimeProvider.System)
            .ReleaseCapacityAsync(ReservationId, slot.Id, 5m, session: null, CancellationToken.None);

        Assert.Equal("FullyBooked", StatusCondition(filter));
    }

    [Fact]
    public async Task Adjust_CompareAndSwapIncludesTheReadAvailabilityStatus()
    {
        // Reducing a claim on an Unavailable slot keeps the filter on Unavailable.
        var database = new MongoTestContext();
        EnergyBookingSlot slot = SlotWithClaim(SlotAvailabilityStatus.Unavailable, available: 0m);
        database.ReturnSlots(slot);
        BsonDocument filter = CaptureSlotUpdateFilter(database, slot);

        await new ReservationCapacityService(database.Context, TimeProvider.System)
            .AdjustHeldCapacityAsync(ReservationId, slot.Id, 1, 5m, 2, 3m, session: null, CancellationToken.None);

        Assert.Equal("Unavailable", StatusCondition(filter));
    }

    private static BsonDocument CaptureSlotUpdateFilter(MongoTestContext database, EnergyBookingSlot slot)
    {
        // Record the rendered FindOneAndUpdate filter and report the update as applied.
        var captured = new BsonDocument();
        database.Slots
            .Setup(collection => collection.FindOneAndUpdateAsync(
                It.IsAny<FilterDefinition<EnergyBookingSlot>>(),
                It.IsAny<UpdateDefinition<EnergyBookingSlot>>(),
                It.IsAny<FindOneAndUpdateOptions<EnergyBookingSlot, EnergyBookingSlot>>(),
                It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<EnergyBookingSlot>, UpdateDefinition<EnergyBookingSlot>,
                FindOneAndUpdateOptions<EnergyBookingSlot, EnergyBookingSlot>, CancellationToken>(
                (filter, _, _, _) => captured.AddRange(filter.Render(new RenderArgs<EnergyBookingSlot>(
                    BsonSerializer.LookupSerializer<EnergyBookingSlot>(),
                    BsonSerializer.SerializerRegistry))))
            .ReturnsAsync(slot);
        return captured;
    }

    private static string StatusCondition(BsonDocument filter)
    {
        // The driver may render an implicit AND or an explicit $and; accept both shapes.
        if (filter.TryGetValue("availability_status", out BsonValue direct))
        {
            return direct.AsString;
        }

        foreach (BsonValue part in filter.GetValue("$and", new BsonArray()).AsBsonArray)
        {
            if (part.AsBsonDocument.TryGetValue("availability_status", out BsonValue nested))
            {
                return nested.AsString;
            }
        }

        throw new Xunit.Sdk.XunitException($"No availability_status condition in {filter.ToJson()}");
    }

    private static EnergyBookingSlot SlotWithClaim(SlotAvailabilityStatus status, decimal available)
    {
        // A slot fully held by one reservation claim of 5 kWh.
        return new EnergyBookingSlot
        {
            Id = ObjectId.GenerateNewId().ToString(),
            StationId = ObjectId.GenerateNewId().ToString(),
            StartTimeUtc = DateTime.UtcNow.AddDays(2),
            EndTimeUtc = DateTime.UtcNow.AddDays(2).AddHours(1),
            TotalCapacityKwh = 5m,
            AvailableCapacityKwh = available,
            AvailabilityStatus = status,
            CapacityAllocations =
            [
                new SlotCapacityAllocation { ReservationId = ReservationId, ReservationVersion = 1, EnergyKwh = 5m }
            ]
        };
    }
}
