/*
 * ReservationSchedulingGuardService.cs
 * -----------------------------------------------------------------------------
 * Purpose : Serializes reservation overlap checks for one prosumer with a
 *           MongoDB-backed lease shared by every API instance.
 * Safety  : Acquisition and release are conditional database writes; no
 *           in-memory lock is used as the concurrency boundary.
 * -----------------------------------------------------------------------------
 */

using MongoDB.Driver;
using SolarMicrogrid.API.Data;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.Entities;

namespace SolarMicrogrid.API.Services;

public sealed record ReservationSchedulingLease(
    string ProsumerNic,
    string LeaseToken);

public sealed class ReservationSchedulingGuardService
{
    private const int MaximumAcquireAttempts = 40;
    private static readonly TimeSpan AcquireRetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    private readonly MongoDbContext _context;
    private readonly TimeProvider _timeProvider;

    public ReservationSchedulingGuardService(MongoDbContext context, TimeProvider timeProvider)
    {
        // Reuse the shared Component 3 scheduling-guard collection.
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<ReservationSchedulingLease> AcquireAsync(
        string prosumerNic,
        CancellationToken cancellationToken)
    {
        // Atomically claim a missing or expired per-prosumer lease with bounded contention retries.
        string normalizedNic = NormalizeNic(prosumerNic);
        string leaseToken = Guid.NewGuid().ToString("N");

        for (int attempt = 0; attempt < MaximumAcquireAttempts; attempt++)
        {
            DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            FilterDefinition<ReservationSchedulingGuard> filter =
                Builders<ReservationSchedulingGuard>.Filter.And(
                    Builders<ReservationSchedulingGuard>.Filter.Eq(
                        guard => guard.ProsumerNic,
                        normalizedNic),
                    Builders<ReservationSchedulingGuard>.Filter.Lte(
                        guard => guard.LeaseExpiresAtUtc,
                        nowUtc));
            UpdateDefinition<ReservationSchedulingGuard> update =
                Builders<ReservationSchedulingGuard>.Update
                    .SetOnInsert(guard => guard.ProsumerNic, normalizedNic)
                    .Set(guard => guard.LeaseToken, leaseToken)
                    .Set(guard => guard.LeaseExpiresAtUtc, nowUtc.Add(LeaseDuration));

            try
            {
                ReservationSchedulingGuard? acquired =
                    await _context.ReservationSchedulingGuards.FindOneAndUpdateAsync(
                        filter,
                        update,
                        new FindOneAndUpdateOptions<ReservationSchedulingGuard>
                        {
                            IsUpsert = true,
                            ReturnDocument = ReturnDocument.After
                        },
                        cancellationToken);

                if (acquired?.LeaseToken == leaseToken)
                {
                    return new ReservationSchedulingLease(normalizedNic, leaseToken);
                }
            }
            catch (MongoWriteException exception)
                when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                // A live lease won the unique NIC race; wait briefly before retrying.
            }
            catch (MongoCommandException exception) when (exception.Code == 11000)
            {
                // findAndModify upserts report the same live-lease race as a command error.
            }

            await Task.Delay(AcquireRetryDelay, cancellationToken);
        }

        throw new ConflictException(
            "Another reservation request for this prosumer is still being processed. Retry shortly.");
    }

    public async Task ReleaseAsync(
        ReservationSchedulingLease lease,
        CancellationToken cancellationToken)
    {
        // Delete only the lease token owned by this operation so stale cleanup cannot unlock newer work.
        ArgumentNullException.ThrowIfNull(lease);
        FilterDefinition<ReservationSchedulingGuard> filter =
            Builders<ReservationSchedulingGuard>.Filter.And(
                Builders<ReservationSchedulingGuard>.Filter.Eq(
                    guard => guard.ProsumerNic,
                    lease.ProsumerNic),
                Builders<ReservationSchedulingGuard>.Filter.Eq(
                    guard => guard.LeaseToken,
                    lease.LeaseToken));

        await _context.ReservationSchedulingGuards.DeleteOneAsync(
            filter,
            cancellationToken);
    }

    private static string NormalizeNic(string? value)
    {
        // Match the uppercase NIC identity convention used by authentication and users.
        string normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Prosumer NIC is required.", nameof(value));
        }

        return normalized;
    }
}
