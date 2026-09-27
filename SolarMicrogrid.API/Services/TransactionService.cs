/*
 * TransactionService.cs
 * -----------------------------------------------------------------------------
 * Purpose : Issues opaque QR secrets, verifies them for assigned operators, and
 *           completes eligible reservations exactly once through MongoDB CAS.
 * Security: Only SHA-256 secret/actor-reference hashes are persisted here; raw
 *           QR and verification secrets exist only in their immediate responses.
 * Ownership: Reuses Member 1 users/roles, Member 2 stations, and Member 3's
 *            Approved -> Completed lifecycle and reservation capacity fields.
 * -----------------------------------------------------------------------------
 */

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using SolarMicrogrid.API.Data;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.DTOs.Transactions;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Settings;

namespace SolarMicrogrid.API.Services;

public sealed class TransactionService : ITransactionService
{
    private const int SecretByteLength = 32;
    private const int MaximumSecretLength = 128;
    private const int MaximumSecretGenerationAttempts = 3;

    private static readonly Regex NicPattern = new(
        "^([0-9]{9}[VX]|[0-9]{12})$",
        RegexOptions.CultureInvariant);

    private static readonly Regex OpaqueSecretPattern = new(
        "^[A-Za-z0-9_-]+$",
        RegexOptions.CultureInvariant);

    private readonly MongoDbContext _context;
    private readonly MongoTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _qrTokenLifetime;
    private readonly TimeSpan _verificationLifetime;

    public TransactionService(
        MongoDbContext context,
        MongoTransactionRunner transactionRunner,
        IOptions<TransactionSettings> settingsOptions,
        TimeProvider timeProvider)
    {
        // Reuse shared persistence/transactions and capture validated server-only lifetimes.
        _context = context;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
        TransactionSettings settings = settingsOptions.Value;
        _qrTokenLifetime = TimeSpan.FromMinutes(settings.QrTokenLifetimeMinutes);
        _verificationLifetime = TimeSpan.FromMinutes(settings.VerificationLifetimeMinutes);
    }

    public async Task<IssueQrTransactionResponseDto> IssueAsync(
        string actorNic,
        string actorRoleClaim,
        string reservationId,
        CancellationToken cancellationToken)
    {
        // Revalidate the active Prosumer and owner-scope before creating any bearer secret.
        string normalizedReservationId = NormalizeObjectId(reservationId, nameof(reservationId));
        User actor = await LoadAndValidateActorAsync(
            actorNic,
            actorRoleClaim,
            UserRole.Prosumer,
            session: null,
            cancellationToken);
        EnergyReservation? reservation = await _context.Reservations
            .Find(item => item.Id == normalizedReservationId && item.ProsumerNic == actor.Nic)
            .FirstOrDefaultAsync(cancellationToken);
        if (reservation is null)
        {
            throw new NotFoundException("The reservation was not found.");
        }

        DateTime serverNowUtc = GetUtcNow();
        await EnsureReservationEligibleAsync(
            reservation,
            expectedVersion: reservation.Version,
            expectedStationId: reservation.StationId,
            expectedOwnerReferenceHash: HashReference(actor.Nic),
            serverNowUtc,
            session: null,
            cancellationToken);

        for (int attempt = 0; attempt < MaximumSecretGenerationAttempts; attempt++)
        {
            string rawToken = GenerateOpaqueSecret();
            var transaction = new QrTransaction
            {
                Id = ObjectId.GenerateNewId().ToString(),
                TokenHash = HashSecret(rawToken),
                ReservationId = reservation.Id,
                ReservationVersion = reservation.Version,
                StationId = reservation.StationId,
                OwnerReferenceHash = HashReference(actor.Nic),
                State = QrTransactionState.Issued,
                IssuedAtUtc = serverNowUtc,
                TokenExpiresAtUtc = serverNowUtc.Add(_qrTokenLifetime),
                UpdatedAtUtc = serverNowUtc
            };

            try
            {
                await _context.QrTransactions.InsertOneAsync(
                    transaction,
                    cancellationToken: cancellationToken);
                return new IssueQrTransactionResponseDto
                {
                    ReservationId = reservation.Id,
                    ReservationVersion = reservation.Version,
                    QrToken = rawToken,
                    IssuedAtUtc = transaction.IssuedAtUtc,
                    ExpiresAtUtc = transaction.TokenExpiresAtUtc
                };
            }
            catch (MongoWriteException exception)
                when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // Regenerate after the cryptographically improbable unique-hash collision.
            }
        }

        throw new InvalidOperationException("A unique QR transaction token could not be generated.");
    }

    public async Task<VerifyQrTransactionResponseDto> VerifyAsync(
        string actorNic,
        string actorRoleClaim,
        VerifyQrTransactionRequestDto request,
        CancellationToken cancellationToken)
    {
        // Consume one valid issued token only after re-reading operator, reservation, and station state.
        ArgumentNullException.ThrowIfNull(request);
        User actor = await LoadAndValidateActorAsync(
            actorNic,
            actorRoleClaim,
            UserRole.GridOperator,
            session: null,
            cancellationToken);
        string rawToken = NormalizeOpaqueSecret(request.Token, nameof(request.Token));
        string tokenHash = HashSecret(rawToken);
        QrTransaction? transaction = await _context.QrTransactions
            .Find(item => item.TokenHash == tokenHash)
            .FirstOrDefaultAsync(cancellationToken);
        if (transaction is null)
        {
            throw new NotFoundException("The QR token is invalid or no longer usable.");
        }

        EnsureOperatorStation(actor, transaction.StationId);
        DateTime serverNowUtc = GetUtcNow();
        await EnsureIssuedTransactionAsync(transaction, serverNowUtc, cancellationToken);
        EnergyReservation reservation = await LoadReservationAsync(
            transaction.ReservationId,
            session: null,
            cancellationToken);

        try
        {
            await EnsureReservationEligibleAsync(
                reservation,
                transaction.ReservationVersion,
                transaction.StationId,
                transaction.OwnerReferenceHash,
                serverNowUtc,
                session: null,
                cancellationToken);
        }
        catch (ConflictException)
        {
            await RevokeIssuedTransactionAsync(transaction.Id, serverNowUtc, cancellationToken);
            throw;
        }

        DateTime verificationExpiresAtUtc = Min(
            serverNowUtc.Add(_verificationLifetime),
            reservation.ScheduledEndTimeUtc);
        for (int attempt = 0; attempt < MaximumSecretGenerationAttempts; attempt++)
        {
            string verificationId = GenerateOpaqueSecret();
            string verificationHash = HashSecret(verificationId);
            FilterDefinition<QrTransaction> filter = Builders<QrTransaction>.Filter.And(
                Builders<QrTransaction>.Filter.Eq(item => item.Id, transaction.Id),
                Builders<QrTransaction>.Filter.Eq(item => item.TokenHash, tokenHash),
                Builders<QrTransaction>.Filter.Eq(item => item.State, QrTransactionState.Issued),
                Builders<QrTransaction>.Filter.Gt(item => item.TokenExpiresAtUtc, serverNowUtc));
            UpdateDefinition<QrTransaction> update = Builders<QrTransaction>.Update
                .Set(item => item.State, QrTransactionState.Verified)
                .Set(item => item.VerificationHash, verificationHash)
                .Set(item => item.VerifiedAtUtc, serverNowUtc)
                .Set(item => item.VerificationExpiresAtUtc, verificationExpiresAtUtc)
                .Set(item => item.VerifiedByOperatorReferenceHash, HashReference(actor.Nic))
                .Set(item => item.UpdatedAtUtc, serverNowUtc);
            var options = new FindOneAndUpdateOptions<QrTransaction>
            {
                ReturnDocument = ReturnDocument.After
            };

            try
            {
                QrTransaction? verified = await _context.QrTransactions.FindOneAndUpdateAsync(
                    filter,
                    update,
                    options,
                    cancellationToken);
                if (verified is null)
                {
                    await ThrowCurrentTransactionConflictAsync(transaction.Id, cancellationToken);
                }

                SolarStationInfo station = await LoadStationAsync(
                    reservation.StationId,
                    session: null,
                    cancellationToken);
                return new VerifyQrTransactionResponseDto
                {
                    VerificationId = verificationId,
                    ReservationId = reservation.Id,
                    ReservationReference = BuildReservationReference(reservation.Id),
                    ReservationVersion = reservation.Version,
                    StationId = reservation.StationId,
                    StationName = station.Name,
                    ScheduledStartTimeUtc = reservation.ScheduledStartTimeUtc,
                    ScheduledEndTimeUtc = reservation.ScheduledEndTimeUtc,
                    RequestedEnergyKwh = reservation.RequestedEnergyKwh,
                    Status = reservation.Status.ToString(),
                    VerifiedAtUtc = serverNowUtc,
                    ExpiresAtUtc = verificationExpiresAtUtc
                };
            }
            catch (MongoWriteException exception)
                when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // Regenerate after the cryptographically improbable verification-hash collision.
            }
        }

        throw new InvalidOperationException("A unique verification receipt could not be generated.");
    }

    public async Task<CompleteQrTransactionResponseDto> CompleteAsync(
        string actorNic,
        string actorRoleClaim,
        string reservationId,
        CompleteQrTransactionRequestDto request,
        CancellationToken cancellationToken)
    {
        // Run reservation and transaction CAS updates in one MongoDB transaction when supported.
        ArgumentNullException.ThrowIfNull(request);
        string normalizedReservationId = NormalizeObjectId(reservationId, nameof(reservationId));
        if (request.ExpectedVersion < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.ExpectedVersion),
                "ExpectedVersion must be positive.");
        }

        string verificationId = NormalizeOpaqueSecret(
            request.VerificationId,
            nameof(request.VerificationId));
        string verificationHash = HashSecret(verificationId);
        DateTime serverNowUtc = GetUtcNow();
        ConsistencyExecutionResult<CompleteQrTransactionResponseDto> result =
            await _transactionRunner.ExecuteAsync(
                (session, token) => CompleteCoreAsync(
                    actorNic,
                    actorRoleClaim,
                    normalizedReservationId,
                    request.ExpectedVersion,
                    verificationHash,
                    serverNowUtc,
                    session,
                    token),
                token => CompleteCoreAsync(
                    actorNic,
                    actorRoleClaim,
                    normalizedReservationId,
                    request.ExpectedVersion,
                    verificationHash,
                    serverNowUtc,
                    session: null,
                    token),
                cancellationToken);
        return result.Value;
    }

    private async Task<CompleteQrTransactionResponseDto> CompleteCoreAsync(
        string actorNic,
        string actorRoleClaim,
        string reservationId,
        long expectedVersion,
        string verificationHash,
        DateTime serverNowUtc,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Re-read all bindings and atomically claim the single eligible completion transition.
        User actor = await LoadAndValidateActorAsync(
            actorNic,
            actorRoleClaim,
            UserRole.GridOperator,
            session,
            cancellationToken);
        QrTransaction? transaction = await FindTransactionByVerificationHashAsync(
            verificationHash,
            session,
            cancellationToken);
        if (transaction is null)
        {
            throw new NotFoundException("The verification receipt is invalid or no longer usable.");
        }

        EnsureCompletionTransactionState(transaction, serverNowUtc);
        EnsureOperatorStation(actor, transaction.StationId);
        if (!string.Equals(transaction.ReservationId, reservationId, StringComparison.Ordinal))
        {
            throw new ConflictException("The verification receipt does not match this reservation.");
        }

        string operatorReferenceHash = HashReference(actor.Nic);
        if (!string.Equals(
                transaction.VerifiedByOperatorReferenceHash,
                operatorReferenceHash,
                StringComparison.Ordinal))
        {
            throw new ForbiddenException(
                "Only the Grid Operator who verified this transaction may complete it.");
        }

        EnergyReservation reservation = await LoadReservationAsync(
            reservationId,
            session,
            cancellationToken);
        if (reservation.Version != expectedVersion ||
            transaction.ReservationVersion != expectedVersion)
        {
            throw new ConflictException("The reservation changed. Scan a new QR token and try again.");
        }

        await EnsureReservationEligibleAsync(
            reservation,
            expectedVersion,
            transaction.StationId,
            transaction.OwnerReferenceHash,
            serverNowUtc,
            session,
            cancellationToken);
        ReservationService.EnsureTransitionAllowed(
            reservation.Status,
            ReservationStatus.Completed);

        ReservationStatusHistoryEntry historyEntry = new()
        {
            FromStatus = reservation.Status,
            ToStatus = ReservationStatus.Completed,
            ChangedAtUtc = serverNowUtc,
            ActorNic = actor.Nic,
            ActorRole = actor.Role,
            Version = reservation.Version + 1,
            Reason = "Completed after server-side QR verification."
        };
        FilterDefinition<EnergyReservation> reservationFilter =
            Builders<EnergyReservation>.Filter.And(
                Builders<EnergyReservation>.Filter.Eq(item => item.Id, reservation.Id),
                Builders<EnergyReservation>.Filter.Eq(item => item.Version, expectedVersion),
                Builders<EnergyReservation>.Filter.Eq(
                    item => item.Status,
                    ReservationStatus.Approved),
                Builders<EnergyReservation>.Filter.Eq(
                    item => item.CapacityState,
                    ReservationCapacityState.Held),
                Builders<EnergyReservation>.Filter.Eq(
                    item => item.StationId,
                    transaction.StationId),
                Builders<EnergyReservation>.Filter.Eq(
                    item => item.ProsumerNic,
                    reservation.ProsumerNic));
        UpdateDefinition<EnergyReservation> reservationUpdate =
            Builders<EnergyReservation>.Update
                .Set(item => item.Status, ReservationStatus.Completed)
                .Set(item => item.Version, expectedVersion + 1)
                .Set(item => item.CapacityState, ReservationCapacityState.Consumed)
                .Set(item => item.CompletedAtUtc, serverNowUtc)
                .Set(item => item.CompletedByActorNic, actor.Nic)
                .Set(item => item.CompletedVerificationId, transaction.Id)
                .Set(item => item.UpdatedAtUtc, serverNowUtc)
                .Set(item => item.UpdatedByActorNic, actor.Nic)
                .Push(item => item.StatusHistory, historyEntry);
        var reservationOptions = new FindOneAndUpdateOptions<EnergyReservation>
        {
            ReturnDocument = ReturnDocument.After
        };
        EnergyReservation? completedReservation = session is null
            ? await _context.Reservations.FindOneAndUpdateAsync(
                reservationFilter,
                reservationUpdate,
                reservationOptions,
                cancellationToken)
            : await _context.Reservations.FindOneAndUpdateAsync(
                session,
                reservationFilter,
                reservationUpdate,
                reservationOptions,
                cancellationToken);
        if (completedReservation is null)
        {
            throw new ConflictException("The reservation was already completed or is no longer eligible.");
        }

        FilterDefinition<QrTransaction> transactionFilter = Builders<QrTransaction>.Filter.And(
            Builders<QrTransaction>.Filter.Eq(item => item.Id, transaction.Id),
            Builders<QrTransaction>.Filter.Eq(item => item.State, QrTransactionState.Verified),
            Builders<QrTransaction>.Filter.Eq(item => item.VerificationHash, verificationHash),
            Builders<QrTransaction>.Filter.Eq(
                item => item.VerifiedByOperatorReferenceHash,
                operatorReferenceHash),
            Builders<QrTransaction>.Filter.Gt(
                item => item.VerificationExpiresAtUtc,
                serverNowUtc));
        UpdateDefinition<QrTransaction> transactionUpdate = Builders<QrTransaction>.Update
            .Set(item => item.State, QrTransactionState.Completed)
            .Set(item => item.CompletedAtUtc, serverNowUtc)
            .Set(item => item.CompletedByOperatorReferenceHash, operatorReferenceHash)
            .Set(item => item.UpdatedAtUtc, serverNowUtc);
        UpdateResult transactionResult = session is null
            ? await _context.QrTransactions.UpdateOneAsync(
                transactionFilter,
                transactionUpdate,
                cancellationToken: cancellationToken)
            : await _context.QrTransactions.UpdateOneAsync(
                session,
                transactionFilter,
                transactionUpdate,
                cancellationToken: cancellationToken);
        if (transactionResult.ModifiedCount != 1)
        {
            throw new ConflictException("The verification receipt was already consumed or expired.");
        }

        return new CompleteQrTransactionResponseDto
        {
            ReservationId = completedReservation.Id,
            ReservationReference = BuildReservationReference(completedReservation.Id),
            StationId = completedReservation.StationId,
            Status = completedReservation.Status.ToString(),
            Version = completedReservation.Version,
            CompletedAtUtc = completedReservation.CompletedAtUtc!.Value
        };
    }

    private async Task<User> LoadAndValidateActorAsync(
        string actorNic,
        string actorRoleClaim,
        UserRole requiredRole,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Match exact JWT claims to one active persisted user with the required workflow role.
        string normalizedNic = NormalizeNic(actorNic, nameof(actorNic));
        FilterDefinition<User> filter = Builders<User>.Filter.Eq(user => user.Nic, normalizedNic);
        User? actor = session is null
            ? await _context.Users.Find(filter).FirstOrDefaultAsync(cancellationToken)
            : await _context.Users.Find(session, filter).FirstOrDefaultAsync(cancellationToken);
        if (actor is null)
        {
            throw new UnauthorizedException("The authenticated user no longer exists.");
        }

        if (!Enum.TryParse(actorRoleClaim, ignoreCase: false, out UserRole claimedRole) ||
            claimedRole != actor.Role)
        {
            throw new ForbiddenException("The authenticated role does not match the current user record.");
        }

        if (actor.Status != UserStatus.Active)
        {
            throw new ForbiddenException("Only active users may use QR transactions.");
        }

        if (actor.Role != requiredRole)
        {
            throw new ForbiddenException($"Only {requiredRole} users may perform this operation.");
        }

        if (requiredRole == UserRole.GridOperator &&
            !ObjectId.TryParse(actor.AssignedStationId, out _))
        {
            throw new ForbiddenException(
                "The Grid Operator does not have a valid assigned station.");
        }

        return actor;
    }

    private async Task EnsureReservationEligibleAsync(
        EnergyReservation reservation,
        long expectedVersion,
        string expectedStationId,
        string expectedOwnerReferenceHash,
        DateTime serverNowUtc,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Require the shared approved/held record and reject ended or changed reservation bindings.
        if (reservation.Status != ReservationStatus.Approved ||
            reservation.CapacityState != ReservationCapacityState.Held ||
            reservation.Version != expectedVersion ||
            reservation.ScheduledEndTimeUtc <= serverNowUtc ||
            !string.Equals(reservation.StationId, expectedStationId, StringComparison.Ordinal) ||
            !string.Equals(
                HashReference(reservation.ProsumerNic),
                expectedOwnerReferenceHash,
                StringComparison.Ordinal))
        {
            throw new ConflictException("The reservation is no longer eligible for QR completion.");
        }

        SolarStationInfo station = await LoadStationAsync(
            reservation.StationId,
            session,
            cancellationToken);
        if (!station.IsActive)
        {
            throw new ConflictException("The reservation station is not active.");
        }
    }

    private async Task EnsureIssuedTransactionAsync(
        QrTransaction transaction,
        DateTime serverNowUtc,
        CancellationToken cancellationToken)
    {
        // Classify replay/final states and persist explicit expiry before verification.
        if (transaction.State != QrTransactionState.Issued)
        {
            ThrowTransactionStateConflict(transaction.State);
        }

        if (transaction.TokenExpiresAtUtc <= serverNowUtc)
        {
            await _context.QrTransactions.UpdateOneAsync(
                item => item.Id == transaction.Id && item.State == QrTransactionState.Issued,
                Builders<QrTransaction>.Update
                    .Set(item => item.State, QrTransactionState.Expired)
                    .Set(item => item.UpdatedAtUtc, serverNowUtc),
                cancellationToken: cancellationToken);
            throw new ConflictException("The QR token has expired.");
        }
    }

    private static void EnsureCompletionTransactionState(
        QrTransaction transaction,
        DateTime serverNowUtc)
    {
        // Accept only an unconsumed verified receipt that remains inside server time.
        if (transaction.State == QrTransactionState.Completed)
        {
            throw new ConflictException("The transfer has already been completed.");
        }

        if (transaction.State != QrTransactionState.Verified)
        {
            ThrowTransactionStateConflict(transaction.State);
        }

        if (!transaction.VerificationExpiresAtUtc.HasValue ||
            transaction.VerificationExpiresAtUtc.Value <= serverNowUtc)
        {
            throw new ConflictException("The verification receipt has expired.");
        }
    }

    private static void EnsureOperatorStation(User actor, string stationId)
    {
        // Enforce the persisted Member 1 assignment against Member 2's station reference.
        if (!string.Equals(actor.AssignedStationId, stationId, StringComparison.Ordinal))
        {
            throw new ForbiddenException(
                "The QR transaction belongs to a different operator station.");
        }
    }

    private async Task<EnergyReservation> LoadReservationAsync(
        string reservationId,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Re-read the authoritative reservation through the caller's transaction snapshot.
        FilterDefinition<EnergyReservation> filter =
            Builders<EnergyReservation>.Filter.Eq(item => item.Id, reservationId);
        EnergyReservation? reservation = session is null
            ? await _context.Reservations.Find(filter).FirstOrDefaultAsync(cancellationToken)
            : await _context.Reservations.Find(session, filter).FirstOrDefaultAsync(cancellationToken);
        return reservation ?? throw new NotFoundException("The reservation was not found.");
    }

    private async Task<SolarStationInfo> LoadStationAsync(
        string stationId,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Reuse the authoritative Member 2 station instead of trusting token/client station data.
        FilterDefinition<SolarStationInfo> filter =
            Builders<SolarStationInfo>.Filter.Eq(item => item.Id, stationId);
        SolarStationInfo? station = session is null
            ? await _context.Stations.Find(filter).FirstOrDefaultAsync(cancellationToken)
            : await _context.Stations.Find(session, filter).FirstOrDefaultAsync(cancellationToken);
        return station ?? throw new ConflictException("The reservation station no longer exists.");
    }

    private async Task<QrTransaction?> FindTransactionByVerificationHashAsync(
        string verificationHash,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Locate a receipt only by its one-way hash and never persist or query the raw value.
        FilterDefinition<QrTransaction> filter =
            Builders<QrTransaction>.Filter.Eq(item => item.VerificationHash, verificationHash);
        return session is null
            ? await _context.QrTransactions.Find(filter).FirstOrDefaultAsync(cancellationToken)
            : await _context.QrTransactions.Find(session, filter).FirstOrDefaultAsync(cancellationToken);
    }

    private Task RevokeIssuedTransactionAsync(
        string transactionId,
        DateTime serverNowUtc,
        CancellationToken cancellationToken)
    {
        // Make a token permanently unusable after its bound reservation becomes ineligible.
        return _context.QrTransactions.UpdateOneAsync(
            item => item.Id == transactionId && item.State == QrTransactionState.Issued,
            Builders<QrTransaction>.Update
                .Set(item => item.State, QrTransactionState.Revoked)
                .Set(item => item.UpdatedAtUtc, serverNowUtc),
            cancellationToken: cancellationToken);
    }

    private async Task ThrowCurrentTransactionConflictAsync(
        string transactionId,
        CancellationToken cancellationToken)
    {
        // Reload after a failed verification CAS to distinguish replay from expiry safely.
        QrTransaction? current = await _context.QrTransactions
            .Find(item => item.Id == transactionId)
            .FirstOrDefaultAsync(cancellationToken);
        if (current is null)
        {
            throw new NotFoundException("The QR token is invalid or no longer usable.");
        }

        ThrowTransactionStateConflict(current.State);
    }

    private static void ThrowTransactionStateConflict(QrTransactionState state)
    {
        // Return stable conflict reasons without exposing stored hashes or actor references.
        throw state switch
        {
            QrTransactionState.Verified =>
                new ConflictException("The QR token has already been verified."),
            QrTransactionState.Completed =>
                new ConflictException("The transfer has already been completed."),
            QrTransactionState.Expired =>
                new ConflictException("The QR token or verification receipt has expired."),
            QrTransactionState.Revoked =>
                new ConflictException("The QR token is no longer eligible."),
            _ => new ConflictException("The QR token is invalid or no longer usable.")
        };
    }

    private static string GenerateOpaqueSecret()
    {
        // Produce 256 bits of cryptographic entropy in URL-safe unpadded form for QR transport.
        byte[] bytes = RandomNumberGenerator.GetBytes(SecretByteLength);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string HashSecret(string secret)
    {
        // Persist a deterministic SHA-256 digest so database disclosure cannot replay the bearer value.
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }

    private static string HashReference(string actorNic)
    {
        // Store only a one-way binding for transaction ownership/operator checks.
        return HashSecret($"actor-reference:{actorNic.Trim().ToUpperInvariant()}");
    }

    private static string NormalizeOpaqueSecret(string? value, string parameterName)
    {
        // Bound and validate the URL-safe opaque value before hashing it for lookup.
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 32 or > MaximumSecretLength ||
            !OpaqueSecretPattern.IsMatch(normalized))
        {
            throw new BadRequestException("A valid opaque transaction value is required.");
        }

        return normalized;
    }

    private static string NormalizeNic(string? value, string parameterName)
    {
        // Normalize the exact NIC claim format already used as the repository user identifier.
        string normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!NicPattern.IsMatch(normalized))
        {
            throw new ArgumentException(
                "NIC must be 9 digits followed by V or X, or 12 digits.",
                parameterName);
        }

        return normalized;
    }

    private static string NormalizeObjectId(string? value, string parameterName)
    {
        // Enforce the repository's ObjectId representation before any database access.
        if (!ObjectId.TryParse(value?.Trim(), out ObjectId objectId))
        {
            throw new ArgumentException("A valid MongoDB ObjectId is required.", parameterName);
        }

        return objectId.ToString();
    }

    private static string BuildReservationReference(string reservationId)
    {
        // Match the established RES plus final-eight-hex display reference without exposing token data.
        string suffix = reservationId.Length <= 8 ? reservationId : reservationId[^8..];
        return $"RES-{suffix.ToUpperInvariant()}";
    }

    private static DateTime Min(DateTime first, DateTime second)
    {
        // Keep verification validity inside both its configured TTL and the reservation interval.
        return first <= second ? first : second;
    }

    private DateTime GetUtcNow()
    {
        // Capture all security time decisions from the injectable authoritative server clock.
        return _timeProvider.GetUtcNow().UtcDateTime;
    }
}
