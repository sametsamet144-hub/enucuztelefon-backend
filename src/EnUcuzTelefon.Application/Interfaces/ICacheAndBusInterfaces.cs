using EnUcuzTelefon.Domain.Entities;
using EnUcuzTelefon.Shared.Results;

namespace EnUcuzTelefon.Application.Interfaces;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
}

public interface IMessageBus
{
    Task PublishAsync<T>(string queueName, T message, CancellationToken cancellationToken = default);
}

public record StoreSyncMessage(
    Guid JobId,
    string StoreSlug,
    DateTime ScheduledTime);

public record PriceAlertCheckMessage(
    Guid VariantId,
    decimal NewLowestPrice);

public record MatchResult(
    bool IsMatched,
    Guid? ProductId,
    Guid? VariantId,
    int ConfidenceScore,
    string Reason);

public interface IProductMatchingEngine
{
    Task<MatchResult> MatchAsync(string storeTitle, string? storeBrand, decimal price, CancellationToken cancellationToken = default);
}

public interface IPriceVerificationService
{
    Task<Result<bool>> VerifyAndRecordPriceChangeAsync(
        Guid variantId,
        Guid storeId,
        Guid? sellerId,
        Guid offerId,
        decimal newPrice,
        decimal shippingCost,
        bool inStock,
        CancellationToken cancellationToken = default);
}

public interface IPriceAlertNotifier
{
    Task CheckAndNotifyAlertsAsync(Guid variantId, decimal currentLowestPrice, CancellationToken cancellationToken = default);
}

public enum VerificationOutcome { Inconclusive = 0, Confirmed = 1, Dead = 2 }

public sealed record LiveOfferVerificationResult(
    bool IsSuccess,
    bool IsUrlAlive,                 // geriye uyumluluk: Outcome != Dead
    decimal? ExtractedPrice,         // SADECE ürün sayfasından okunduysa dolu
    string? ResolvedUrl,
    string? ExtractedTitle,
    string DetectionSource,
    VerificationOutcome Outcome = VerificationOutcome.Inconclusive,
    bool PriceFromProductPage = false,
    bool? InStock = null);

public interface ILiveOfferVerificationEngine
{
    Task<LiveOfferVerificationResult> VerifyAndExtractOfferAsync(
        string url,
        string storeSlug,
        string phoneName,
        int? storageGb,
        decimal currentPrice,
        CancellationToken cancellationToken = default);
}
