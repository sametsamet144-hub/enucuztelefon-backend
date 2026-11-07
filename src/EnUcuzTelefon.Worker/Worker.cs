using EnUcuzTelefon.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace EnUcuzTelefon.Worker;

public class Worker : ScrapingBackgroundWorker
{
    public Worker(IServiceProvider serviceProvider, ILogger<ScrapingBackgroundWorker> logger)
        : base(serviceProvider, logger)
    {
    }
}
