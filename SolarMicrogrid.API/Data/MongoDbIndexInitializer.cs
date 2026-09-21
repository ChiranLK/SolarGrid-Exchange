namespace SolarMicrogrid.API.Data;

public sealed class MongoDbIndexInitializer(MongoDbContext dbContext) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return dbContext.EnsureIndexesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
