using EnUcuzTelefon.Application.Interfaces;
using EnUcuzTelefon.Domain.Entities;
using EnUcuzTelefon.Infrastructure.Adapters;
using EnUcuzTelefon.Infrastructure.Persistence;
using EnUcuzTelefon.IntegrationTests;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnUcuzTelefon.IntegrationTests;

public class WorkerScrapingVerificationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public WorkerScrapingVerificationTests(CustomWebApplicationFactory factory)
    {
        factory.EnsureSeeded();
        _factory = factory;
    }

    [Fact]
    public async Task VerifyScrapingData_97PhonesAcross3Stores_ShouldReturn291ValidOffersWithPricesAndUrls()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // 1. Verify 97 phone models exist in the database catalog
        var products = await db.Products
            .Include(p => p.Brand)
            .Include(p => p.Variants)
            .OrderBy(p => p.Name)
            .ToListAsync();

        products.Should().HaveCount(97, "the seeded database catalog must contain exactly 97 phone models");

        // 2. Setup adapters for 3 main stores: Trendyol, Hepsiburada, n11
        var httpClient = new HttpClient();
        var adapters = new List<IStoreAdapter>
        {
            new TrendyolAdapter(NullLogger<TrendyolAdapter>.Instance, httpClient),
            new HepsiburadaAdapter(NullLogger<HepsiburadaAdapter>.Instance, httpClient),
            new N11Adapter(NullLogger<N11Adapter>.Instance, httpClient)
        };

        var stores = await db.Stores.ToListAsync();
        stores.Should().NotBeEmpty();

        var collectedOffers = new List<Offer>();

        // 3. Collect offers for each phone across the 3 stores
        foreach (var product in products)
        {
            var variant = product.Variants.FirstOrDefault();
            variant.Should().NotBeNull($"Product {product.Name} must have at least one variant");

            foreach (var adapter in adapters)
            {
                var store = stores.FirstOrDefault(s => s.Slug == adapter.StoreSlug)
                            ?? stores.First();

                var searchQuery = BaseStoreAdapter.BuildProductSearchQuery(product.Brand?.Name, product.Name, variant!.StorageGb);
                var searchResult = await adapter.SearchProductsAsync(searchQuery, CancellationToken.None);

                searchResult.IsSuccess.Should().BeTrue();

                if (searchResult.Value.Any())
                {
                    var raw = searchResult.Value.First();

                    // Validate individual data fields
                    raw.Price.Should().BeGreaterThan(0, $"Product '{product.Name}' in store '{adapter.StoreSlug}' must have price > 0");
                    raw.Url.Should().NotBeNullOrWhiteSpace($"Product '{product.Name}' in store '{adapter.StoreSlug}' must have valid URL");
                    raw.Url.Should().StartWith("http", $"Product '{product.Name}' in store '{adapter.StoreSlug}' URL must start with http");

                    var offer = adapter.NormalizeOffer(raw, variant.Id, store.Id, null);
                    collectedOffers.Add(offer);
                }
            }
        }

        // 4. Assert that collected real offers are valid
        collectedOffers.Should().NotBeEmpty();

        var validPricesCount = collectedOffers.Count(o => o.Price > 0);
        validPricesCount.Should().Be(291, "all 291 offers must contain valid prices greater than 0");

        var validUrlsCount = collectedOffers.Count(o => !string.IsNullOrWhiteSpace(o.Url) && o.Url.StartsWith("http"));
        validUrlsCount.Should().Be(291, "all 291 offers must contain valid URLs starting with http");
    }
}
