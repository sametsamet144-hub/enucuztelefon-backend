using EnUcuzTelefon.Domain.Entities;
using EnUcuzTelefon.Shared.Results;

namespace EnUcuzTelefon.Application.Interfaces;

public record StoreRawProduct(
    string ExternalId,
    string Title,
    string Url,
    decimal Price,
    string? SellerName,
    bool InStock,
    string? ImageUrl,
    string? Brand,
    string? Model,
    int? RamGb,
    int? StorageGb,
    string? Color,
    string? RawJson);

public record NormalizedProductOffer(
    string ExternalId,
    string NormalizedTitle,
    string BrandName,
    string ModelName,
    int RamGb,
    int StorageGb,
    string Color,
    decimal Price,
    decimal? OriginalPrice,
    decimal ShippingCost,
    decimal TotalCost,
    bool InStock,
    string Url,
    string? SellerName,
    double SellerRating,
    bool IsOfficial,
    string? ShippingText,
    string? GuaranteeText);

public record StoreIntegrationStatus(
    string StoreSlug,
    bool IsConnected,
    string Message,
    int LatencyMs,
    DateTime CheckedAt);

public interface IStoreAdapter
{
    string StoreSlug { get; }
    string StoreName { get; }

    Task<Result<StoreIntegrationStatus>> TestConnectionAsync(CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<StoreRawProduct>>> SearchProductsAsync(string query, CancellationToken cancellationToken = default);
    Task<Result<StoreRawProduct>> FetchProductDetailsAsync(string externalId, CancellationToken cancellationToken = default);
    Task<Result<decimal>> FetchPricesAsync(string externalId, CancellationToken cancellationToken = default);
    Task<Result<bool>> FetchStockAsync(string externalId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyDictionary<string, string>>> FetchSpecificationsAsync(string externalId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<string>>> FetchImagesAsync(string externalId, CancellationToken cancellationToken = default);
    NormalizedProductOffer NormalizeProduct(StoreRawProduct raw);
    Offer NormalizeOffer(StoreRawProduct raw, Guid variantId, Guid storeId, Guid? sellerId);
    bool ValidateProductUrl(string url);
    Task<Result<StoreIntegrationStatus>> GetIntegrationStatusAsync(CancellationToken cancellationToken = default);
}

public interface IStoreAdapterFactory
{
    IStoreAdapter GetAdapter(string storeSlug);
    IEnumerable<IStoreAdapter> GetAllAdapters();
}

public record AggregatedOfferItem(
    string StoreSlug,
    string StoreName,
    string SellerName,
    decimal Price,
    string Url,
    bool IsDirectProductUrl,
    string? Title = null,
    int StorageGb = 0);

public interface IMarketOfferAggregator
{
    Task<IReadOnlyList<AggregatedOfferItem>> FetchOffersForQueryAsync(string query, string? knownSlug = null, CancellationToken cancellationToken = default);
}
