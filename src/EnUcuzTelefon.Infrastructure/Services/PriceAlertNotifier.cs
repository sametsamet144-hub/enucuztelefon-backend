using EnUcuzTelefon.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnUcuzTelefon.Infrastructure.Services;

public class PriceAlertNotifier : IPriceAlertNotifier
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<PriceAlertNotifier> _logger;

    public PriceAlertNotifier(IApplicationDbContext context, ILogger<PriceAlertNotifier> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task CheckAndNotifyAlertsAsync(Guid variantId, decimal currentLowestPrice, CancellationToken cancellationToken = default)
    {
        var activeAlerts = await _context.PriceAlerts
            .Include(a => a.Product)
            .Include(a => a.Variant)
            .Where(a => a.VariantId == variantId && a.IsActive && !a.IsTriggered && a.TargetPrice >= currentLowestPrice)
            .ToListAsync(cancellationToken);

        if (!activeAlerts.Any()) return;

        foreach (var alert in activeAlerts)
        {
            alert.IsTriggered = true;
            alert.TriggeredAt = DateTime.UtcNow;
            alert.TriggeredPrice = currentLowestPrice;
            alert.NotificationCount++;

            _logger.LogInformation(
                "PRICE ALERT TRIGGERED: Phone '{PhoneName}' variant {Ram}GB/{Storage}GB dropped to {CurrentPrice} TRY! Target was {TargetPrice} TRY. Email notification queued for {Email}",
                alert.Product.Name,
                alert.Variant.RamGb,
                alert.Variant.StorageGb,
                currentLowestPrice,
                alert.TargetPrice,
                alert.Email);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
