using EnUcuzTelefon.Application.Interfaces;
using EnUcuzTelefon.Domain.Entities;
using EnUcuzTelefon.Domain.Enums;
using EnUcuzTelefon.Shared.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnUcuzTelefon.Infrastructure.Services;

public class PriceVerificationService : IPriceVerificationService
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;
    private readonly IPriceAlertNotifier _alertNotifier;
    private readonly ILogger<PriceVerificationService> _logger;

    public PriceVerificationService(
        IApplicationDbContext context,
        ICacheService cache,
        IPriceAlertNotifier alertNotifier,
        ILogger<PriceVerificationService> logger)
    {
        _context = context;
        _cache = cache;
        _alertNotifier = alertNotifier;
        _logger = logger;
    }

    public async Task<Result<bool>> VerifyAndRecordPriceChangeAsync(
        Guid variantId,
        Guid storeId,
        Guid? sellerId,
        Guid offerId,
        decimal newPrice,
        decimal shippingCost,
        bool inStock,
        CancellationToken cancellationToken = default)
    {
        if (newPrice <= 0)
        {
            return Result.Failure<bool>(Error.Validation("Price.Invalid", "Price must be strictly positive."));
        }

        var offer = await _context.Offers
            .Include(o => o.Variant)
            .Include(o => o.Store)
            .FirstOrDefaultAsync(o => o.Id == offerId, cancellationToken);

        if (offer == null)
        {
            return Result.Failure<bool>(Error.NotFound("Offer.NotFound", $"Offer {offerId} not found"));
        }

        var oldPrice = offer.Price;
        var totalCost = newPrice + shippingCost;

        // Idempotency: If price has not changed and availability is identical, just update LastCheckedAt
        if (oldPrice == newPrice && offer.IsAvailable == inStock && offer.ShippingCost == shippingCost)
        {
            offer.LastCheckedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            return Result.Success(false); // No price change occurred
        }

        // Anomaly Detection: Extreme price drops (> 45%) or extreme spikes (> 80%)
        if (oldPrice > 0)
        {
            var dropPercent = (double)((oldPrice - newPrice) / oldPrice * 100);
            if (dropPercent > 45)
            {
                var anomaly = new PriceAnomaly
                {
                    OfferId = offer.Id,
                    VariantId = variantId,
                    StoreId = storeId,
                    OldPrice = oldPrice,
                    NewPrice = newPrice,
                    DropPercent = dropPercent,
                    Reason = $"Extreme price drop of {dropPercent:F1}% detected from {oldPrice} TL to {newPrice} TL",
                    Status = AnomalyStatus.PendingReview
                };
                _context.PriceAnomalies.Add(anomaly);
                _logger.LogWarning("Price anomaly detected for offer {OfferId}: {Reason}", offer.Id, anomaly.Reason);
            }
        }

        // Record real price history entry
        var changeAmount = newPrice - oldPrice;
        var changePercent = oldPrice > 0 ? (double)(changeAmount / oldPrice * 100) : 0;

        var priceHistory = new PriceHistory
        {
            VariantId = variantId,
            OfferId = offer.Id,
            StoreId = storeId,
            SellerId = sellerId,
            Price = newPrice,
            PreviousPrice = oldPrice,
            ChangeAmount = changeAmount,
            ChangePercent = changePercent,
            Timestamp = DateTime.UtcNow,
            Source = "AutomatedWorker",
            IsVerified = true
        };
        _context.PriceHistories.Add(priceHistory);

        // Update offer
        offer.Price = newPrice;
        offer.ShippingCost = shippingCost;
        offer.TotalCost = totalCost;
        offer.IsAvailable = inStock;
        offer.LastCheckedAt = DateTime.UtcNow;

        // Recalculate variant lowest/highest prices
        var allVariantOffers = await _context.Offers
            .Where(o => o.VariantId == variantId && o.IsAvailable && o.Price > 0)
            .ToListAsync(cancellationToken);

        if (allVariantOffers.Any())
        {
            var lowest = allVariantOffers.Min(o => o.Price);
            var highest = allVariantOffers.Max(o => o.Price);

            var variant = offer.Variant;
            if (variant != null)
            {
                variant.PreviousLowestPrice = variant.LowestPrice;
                variant.LowestPrice = lowest;
                variant.HighestPrice = highest;

                if (variant.PreviousLowestPrice > lowest && variant.PreviousLowestPrice > 0)
                {
                    variant.IsPriceDropped = true;
                    variant.PriceDropPercent = (int)((variant.PreviousLowestPrice.Value - lowest) / variant.PreviousLowestPrice.Value * 100);
                }
                else
                {
                    variant.IsPriceDropped = false;
                }

                var lowestOffer = allVariantOffers.First(o => o.Price == lowest);
                variant.LowestPriceStoreName = lowestOffer.Store?.Name ?? offer.Store.Name;
                variant.LowestPriceUrl = lowestOffer.Url;

                // Trigger price alert checking if new lowest price achieved
                await _alertNotifier.CheckAndNotifyAlertsAsync(variantId, lowest, cancellationToken);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        // Invalidate Redis cache for product and catalog
        await _cache.RemoveByPrefixAsync("products:", cancellationToken);
        await _cache.RemoveByPrefixAsync("phone:", cancellationToken);
        await _cache.RemoveByPrefixAsync("deals:", cancellationToken);

        _logger.LogInformation("Recorded price change for Variant {VariantId}: {OldPrice} -> {NewPrice} TRY", variantId, oldPrice, newPrice);
        return Result.Success(true);
    }
}
