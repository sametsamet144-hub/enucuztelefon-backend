using EnUcuzTelefon.Application.Interfaces;
using EnUcuzTelefon.Domain.Entities;
using EnUcuzTelefon.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quartz;

namespace EnUcuzTelefon.Scheduler.Jobs;

[DisallowConcurrentExecution]
public class TwoHourStoreSyncJob : IJob
{
    private readonly IApplicationDbContext _context;
    private readonly IMessageBus _bus;
    private readonly ILogger<TwoHourStoreSyncJob> _logger;

    public TwoHourStoreSyncJob(
        IApplicationDbContext context,
        IMessageBus bus,
        ILogger<TwoHourStoreSyncJob> logger)
    {
        _context = context;
        _bus = bus;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("Quartz.NET 2-Hour Sync Job triggered at {Time} (Europe/Istanbul)", DateTime.UtcNow);

        var activeStores = await _context.Stores
            .Where(s => s.Status == StoreStatus.Active && s.IsActive)
            .ToListAsync(context.CancellationToken);

        _logger.LogInformation("Found {Count} active stores to sync.", activeStores.Count);

        foreach (var store in activeStores)
        {
            var job = new ScrapingJob
            {
                StoreId = store.Id,
                JobType = "ScheduledTwoHourSync",
                Status = JobStatus.Pending,
                StartedAt = DateTime.UtcNow
            };

            _context.ScrapingJobs.Add(job);
            await _context.SaveChangesAsync(context.CancellationToken);

            // Dispatch message to RabbitMQ queue for worker consumption
            await _bus.PublishAsync(
                "store-sync-queue",
                new StoreSyncMessage(job.Id, store.Slug, DateTime.UtcNow),
                context.CancellationToken);

            _logger.LogInformation("Dispatched sync job {JobId} for store '{StoreSlug}'", job.Id, store.Slug);
        }
    }
}
