/*
 * MongoTransactionRunner.cs
 * -----------------------------------------------------------------------------
 * Purpose : Runs multi-document reservation work in a supported MongoDB
 *           transaction and selects an explicit compensation workflow on a
 *           standalone topology that rejects transactions.
 * Safety  : Fallback occurs only for MongoDB's definitive transaction-not-
 *           supported error; ambiguous/network failures are never replayed here.
 * -----------------------------------------------------------------------------
 */

using MongoDB.Driver;

namespace SolarMicrogrid.API.Services;

public sealed record ConsistencyExecutionResult<T>(
    T Value,
    bool UsedTransaction,
    bool UsedCompensation);

public sealed class MongoTransactionRunner
{
    private const int TransactionsNotSupportedErrorCode = 20;

    private readonly IMongoClient _mongoClient;

    public MongoTransactionRunner(IMongoClient mongoClient)
    {
        // Reuse the singleton client so transaction sessions share the configured deployment.
        _mongoClient = mongoClient;
    }

    public async Task<ConsistencyExecutionResult<T>> ExecuteAsync<T>(
        Func<IClientSessionHandle, CancellationToken, Task<T>> transactionalWork,
        Func<CancellationToken, Task<T>> compensatingWork,
        CancellationToken cancellationToken)
    {
        // Prefer an ACID transaction and use compensation only when topology support is definitively absent.
        ArgumentNullException.ThrowIfNull(transactionalWork);
        ArgumentNullException.ThrowIfNull(compensatingWork);

        using IClientSessionHandle session = await _mongoClient.StartSessionAsync(
            cancellationToken: cancellationToken);

        var transactionOptions = new TransactionOptions(
            readConcern: ReadConcern.Snapshot,
            readPreference: ReadPreference.Primary,
            writeConcern: WriteConcern.WMajority);

        try
        {
            T result = await session.WithTransactionAsync(
                transactionalWork,
                transactionOptions,
                cancellationToken);

            return new ConsistencyExecutionResult<T>(
                result,
                UsedTransaction: true,
                UsedCompensation: false);
        }
        catch (MongoCommandException exception)
            when (IsTransactionUnsupported(exception))
        {
            T result = await compensatingWork(cancellationToken);
            return new ConsistencyExecutionResult<T>(
                result,
                UsedTransaction: false,
                UsedCompensation: true);
        }
    }

    private static bool IsTransactionUnsupported(MongoCommandException exception)
    {
        // Restrict fallback to the standalone-server error raised before a transaction can commit.
        return exception.Code == TransactionsNotSupportedErrorCode &&
               string.Equals(exception.CodeName, "IllegalOperation", StringComparison.Ordinal);
    }
}
