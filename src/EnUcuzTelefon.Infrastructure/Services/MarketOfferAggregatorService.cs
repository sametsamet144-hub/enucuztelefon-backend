using EnUcuzTelefon.Application.Interfaces;
using EnUcuzTelefon.Infrastructure.Adapters;

namespace EnUcuzTelefon.Infrastructure.Services;

public class MarketOfferAggregatorService : IMarketOfferAggregator
{
    public async Task<IReadOnlyList<AggregatedOfferItem>> FetchOffersForQueryAsync(string query, string? knownSlug = null, CancellationToken cancellationToken = default)
    {
        var rawOffers = await MarketAggregator.FetchMarketOffersForQueryAsync(query, knownSlug, cancellationToken);
        return rawOffers.Select(o => new AggregatedOfferItem(
            o.StoreSlug,
            o.StoreName,
            o.SellerName,
            o.Price,
            o.Url,
            o.IsDirectProductUrl,
            o.Title,
            o.StorageGb
        )).ToList();
    }
}
