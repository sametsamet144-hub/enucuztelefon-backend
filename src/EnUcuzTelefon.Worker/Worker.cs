using EnUcuzTelefon.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnUcuzTelefon.Worker;

public class Worker : ScrapingBackgroundWorker
{
    public Worker(
        IServiceScopeFactory scopeFactory,
        IOptions<ScrapingWorkerOptions> options,
        ILogger<ScrapingBackgroundWorker> logger)
        : base(scopeFactory, options, logger)
    {
    }
}
