using System.Net;
using System.Net.Http.Json;
using EnUcuzTelefon.Application.DTOs;
using EnUcuzTelefon.Infrastructure.Identity;
using EnUcuzTelefon.Infrastructure.Persistence;
using EnUcuzTelefon.Shared.Results;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnUcuzTelefon.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private static readonly object _lock = new();
    private static bool _seeded = false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.ConfigureServices(services =>
        {
            // Replace DbContext with InMemory for integration tests
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));

            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Remove background workers so they don't block tests
            var hostedServices = services.Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)).ToList();
            foreach (var hs in hostedServices)
            {
                services.Remove(hs);
            }

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseInMemoryDatabase("IntegrationTestsDb");
            });
        });
    }

    public void EnsureSeeded()
    {
        lock (_lock)
        {
            if (_seeded) return;
            _ = CreateClient(); // Ensures web host is started and Program.cs runs
            _seeded = true;
        }
    }
}

public class ApiIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ApiIntegrationTests(CustomWebApplicationFactory factory)
    {
        factory.EnsureSeeded();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthEndpoint_ShouldReturnHealthy()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetProducts_ShouldReturnOkWithItems()
    {
        var response = await _client.GetAsync("/api/v1/products");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<PhoneDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().NotBeNull();
    }

    [Fact]
    public async Task GetBrands_ShouldReturnOkWithBrands()
    {
        var response = await _client.GetAsync("/api/v1/brands");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var brands = await response.Content.ReadFromJsonAsync<List<BrandDto>>();
        brands.Should().NotBeNull();
        brands!.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetStores_ShouldReturnOkWithStores()
    {
        var response = await _client.GetAsync("/api/v1/stores");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var stores = await response.Content.ReadFromJsonAsync<List<StoreDto>>();
        stores.Should().NotBeNull();
        stores!.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreatePriceAlert_WithValidData_ShouldReturnCreatedAlert()
    {
        var request = new CreatePriceAlertRequestDto(
            PhoneSlug: "iphone-16-pro-max",
            TargetPrice: 70000m,
            Email: "testuser@enucuztelefon.com");

        var response = await _client.PostAsJsonAsync("/api/v1/price-alerts", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var alert = await response.Content.ReadFromJsonAsync<PriceAlertDto>();
        alert.Should().NotBeNull();
        alert!.TargetPrice.Should().Be(70000m);
        alert.Email.Should().Be("testuser@enucuztelefon.com");
    }

    [Fact]
    public async Task GetProductBySlug_ShouldReturnDetailedPhoneWithOffersAndHistory()
    {
        var response = await _client.GetAsync("/api/v1/products/iphone-16-pro-max");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var phone = await response.Content.ReadFromJsonAsync<PhoneDto>();
        phone.Should().NotBeNull();
        phone!.Slug.Should().Be("iphone-16-pro-max");
        phone.StoresPrices.Should().NotBeEmpty();
        phone.LowestPrice.Should().BeGreaterThan(0);

        var historyResponse = await _client.GetAsync("/api/v1/products/iphone-16-pro-max/price-history");
        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await historyResponse.Content.ReadFromJsonAsync<List<PriceHistoryDto>>();
        history.Should().NotBeNull();
        history!.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetAdminDashboard_ShouldReturnRealMetrics()
    {
        var response = await _client.GetAsync("/api/v1/admin/dashboard");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var stats = await response.Content.ReadFromJsonAsync<AdminDashboardStatsDto>();
        stats.Should().NotBeNull();
        stats!.TotalPhones.Should().Be(97);
        stats.TotalBrands.Should().BeGreaterThanOrEqualTo(30);
        stats.TotalStores.Should().Be(20);
        stats.TotalOffers.Should().BeGreaterThanOrEqualTo(90);
    }

    [Fact]
    public async Task CompareProducts_ShouldReturnRequestedModels()
    {
        var response = await _client.GetAsync("/api/v1/compare?slugs=iphone-16-pro-max,galaxy-s24-ultra");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var phones = await response.Content.ReadFromJsonAsync<List<PhoneDto>>();
        phones.Should().NotBeNull();
        phones!.Count.Should().Be(2);
    }

    [Fact]
    public async Task GetProductByAliasSlug_ShouldResolveAndReturnValidStoreUrlsAndBenchmarks()
    {
        var response = await _client.GetAsync("/api/v1/products/apple-iphone-16-pro-max");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var phone = await response.Content.ReadFromJsonAsync<PhoneDto>();
        phone.Should().NotBeNull();
        phone!.Slug.Should().Be("iphone-16-pro-max");
        phone.Specs.Performance.AntutuScore.Should().BeGreaterThan(1000000);
        phone.StoresPrices.Should().NotBeEmpty();
        phone.StoresPrices.All(sp => !sp.Url.Contains(".com.tr/urun/")).Should().BeTrue();
    }

    [Fact]
    public async Task RedirectEndpoint_ShouldReturnVerifiedStoreTargetUrl()
    {
        var response = await _client.GetAsync("/api/v1/redirect?store=trendyol&product=iPhone+16+Pro+Max&mode=json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("https://www.trendyol.com/sr?q=");
    }

    [Fact]
    public async Task GetPricesByPhoneId_WithValidPhoneId_ShouldReturnPricesAndLinksExclusivelyForThatPhone()
    {
        // 1. First get a phone to obtain its GUID ID
        var phoneResponse = await _client.GetAsync("/api/v1/products/iphone-16-pro-max");
        phoneResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var phone = await phoneResponse.Content.ReadFromJsonAsync<PhoneDto>();
        phone.Should().NotBeNull();
        var phoneGuid = Guid.Parse(phone!.Id);

        // 2. Call new endpoint by ID: GET /api/v1/products/{id}/prices
        var pricesResponse = await _client.GetAsync($"/api/v1/products/{phoneGuid}/prices");
        pricesResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await pricesResponse.Content.ReadFromJsonAsync<PhonePricesResponseDto>();
        result.Should().NotBeNull();
        result!.PhoneId.Should().Be(phoneGuid);
        result.PhoneName.Should().Be(phone.Name);
        result.StorePrices.Should().NotBeEmpty();

        // 3. Verify every store price entry contains store name, price and direct purchase URL
        foreach (var offer in result.StorePrices)
        {
            offer.Store.Should().NotBeNullOrWhiteSpace();
            offer.Price.Should().BeGreaterThan(0);
            offer.Url.Should().NotBeNullOrWhiteSpace();
            offer.Url.Should().StartWith("http");
        }

        // 4. Verify alias route: GET /api/v1/phones/{id}/prices
        var aliasResponse = await _client.GetAsync($"/api/v1/phones/{phoneGuid}/prices");
        aliasResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Verify query param route: GET /api/v1/products/prices?phoneId=...
        var queryResponse = await _client.GetAsync($"/api/v1/products/prices?phoneId={phoneGuid}");
        queryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetPricesByPhoneId_WithNonExistentId_ShouldReturnNotFound()
    {
        var nonExistentId = Guid.NewGuid();
        var response = await _client.GetAsync($"/api/v1/products/{nonExistentId}/prices");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
