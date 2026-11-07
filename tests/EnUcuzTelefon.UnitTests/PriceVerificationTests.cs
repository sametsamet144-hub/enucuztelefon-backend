using EnUcuzTelefon.Application.Interfaces;
using EnUcuzTelefon.Domain.Entities;
using EnUcuzTelefon.Infrastructure.Persistence;
using EnUcuzTelefon.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EnUcuzTelefon.UnitTests;

public class PriceVerificationTests
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<ICacheService> _cacheMock;
    private readonly Mock<IPriceAlertNotifier> _alertNotifierMock;
    private readonly PriceVerificationService _sut;

    public PriceVerificationTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _cacheMock = new Mock<ICacheService>();
        _alertNotifierMock = new Mock<IPriceAlertNotifier>();

        _sut = new PriceVerificationService(
            _context,
            _cacheMock.Object,
            _alertNotifierMock.Object,
            NullLogger<PriceVerificationService>.Instance);
    }

    [Fact]
    public async Task WhenPriceChanges_ShouldCreatePriceHistoryAndRecordDrop()
    {
        // Arrange
        var variant = new ProductVariant
        {
            Sku = "TEST-SKU",
            LowestPrice = 75000m,
            HighestPrice = 75000m,
            RamGb = 8,
            StorageGb = 256
        };
        var store = new Store { Name = "Trendyol", Slug = "trendyol", Website = "https://trendyol.com" };
        var offer = new Offer
        {
            Variant = variant,
            Store = store,
            Price = 75000m,
            TotalCost = 75000m,
            IsAvailable = true
        };

        _context.ProductVariants.Add(variant);
        _context.Stores.Add(store);
        _context.Offers.Add(offer);
        await _context.SaveChangesAsync();

        // Act - Price drops to 70000m
        var result = await _sut.VerifyAndRecordPriceChangeAsync(
            variant.Id,
            store.Id,
            null,
            offer.Id,
            newPrice: 70000m,
            shippingCost: 0m,
            inStock: true);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue(); // price changed

        var updatedOffer = await _context.Offers.FirstAsync(o => o.Id == offer.Id);
        updatedOffer.Price.Should().Be(70000m);

        var history = await _context.PriceHistories.FirstOrDefaultAsync(h => h.OfferId == offer.Id);
        history.Should().NotBeNull();
        history!.Price.Should().Be(70000m);
        history.PreviousPrice.Should().Be(75000m);
        history.ChangeAmount.Should().Be(-5000m);

        var updatedVariant = await _context.ProductVariants.FirstAsync(v => v.Id == variant.Id);
        updatedVariant.LowestPrice.Should().Be(70000m);
        updatedVariant.IsPriceDropped.Should().BeTrue();
        updatedVariant.PriceDropPercent.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task WhenPriceIsIdentical_ShouldBeIdempotentAndNotCreateDuplicateHistory()
    {
        // Arrange
        var variant = new ProductVariant { Sku = "SKU-2", LowestPrice = 50000m };
        var store = new Store { Name = "Amazon", Slug = "amazon", Website = "https://amazon.com.tr" };
        var offer = new Offer
        {
            Variant = variant,
            Store = store,
            Price = 50000m,
            ShippingCost = 0m,
            TotalCost = 50000m,
            IsAvailable = true
        };

        _context.ProductVariants.Add(variant);
        _context.Stores.Add(store);
        _context.Offers.Add(offer);
        await _context.SaveChangesAsync();

        // Act - Call with exact same price
        var result = await _sut.VerifyAndRecordPriceChangeAsync(
            variant.Id,
            store.Id,
            null,
            offer.Id,
            newPrice: 50000m,
            shippingCost: 0m,
            inStock: true);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse(); // No change occurred

        var historyCount = await _context.PriceHistories.CountAsync(h => h.OfferId == offer.Id);
        historyCount.Should().Be(0); // No redundant record!
    }

    [Fact]
    public async Task WhenExtremePriceDropOccurs_ShouldRecordAnomaly()
    {
        // Arrange - Drop > 45% (e.g. from 100,000 to 20,000)
        var variant = new ProductVariant { Sku = "SKU-ANOMALY", LowestPrice = 100000m };
        var store = new Store { Name = "Teknosa", Slug = "teknosa", Website = "https://teknosa.com" };
        var offer = new Offer { Variant = variant, Store = store, Price = 100000m, TotalCost = 100000m, IsAvailable = true };

        _context.ProductVariants.Add(variant);
        _context.Stores.Add(store);
        _context.Offers.Add(offer);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.VerifyAndRecordPriceChangeAsync(
            variant.Id,
            store.Id,
            null,
            offer.Id,
            newPrice: 20000m, // 80% drop!
            shippingCost: 0m,
            inStock: true);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var anomaly = await _context.PriceAnomalies.FirstOrDefaultAsync(a => a.OfferId == offer.Id);
        anomaly.Should().NotBeNull();
        anomaly!.DropPercent.Should().Be(80.0);
    }
}
