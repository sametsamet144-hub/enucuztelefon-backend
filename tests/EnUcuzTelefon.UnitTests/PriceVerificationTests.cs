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

    [Theory]
    [InlineData("75.798 TL", 75798.0)]
    [InlineData("75.798,00 TL", 75798.0)]
    [InlineData("75,798.00 ₺", 75798.0)]
    [InlineData("69999.00", 69999.0)]
    [InlineData("70,181.05 TL", 70181.05)]
    [InlineData("109.899 TRY", 109899.0)]
    [InlineData("118.999,00 TL", 118999.0)]
    [InlineData("75798,50", 75798.50)]
    public void ParseTurkishPrice_ShouldCorrectlyParseVariousFormats(string rawPrice, decimal expected)
    {
        var parsed = LiveOfferVerificationEngine.ParseTurkishPrice(rawPrice);
        parsed.Should().NotBeNull();
        parsed!.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("https://www.hepsiburada.com/iphone-15-128-gb-p-HBCV921E9E11?magaza=Hepsiburada", true)]
    [InlineData("https://www.hepsiburada.com/redmi-note-13-pro-5g-512-gb-p-HBCV2DAF0546?magaza=Hepsiburada", true)]
    [InlineData("https://www.ciceksepeti.com/iphone-15-128-gb-kc681457071", true)]
    [InlineData("https://www.teknosa.com/iphone-15-128gb-p-515058567?shopId=teknosa", true)]
    [InlineData("https://www.hepsiburada.com/apple-iphone-15-128-gb-mavi-p-HBCV00004X9ZCK?magaza=macinternet", false)]
    [InlineData("https://www.amazon.com.tr/dp/B0CHXGB3NG", false)]
    [InlineData("https://www.teknosa.com/apple-iphone-15-128gb-siyah-akilli-telefon-p-125079197", false)]
    [InlineData("https://www.trendyol.com/apple/iphone-15-128-gb-siyah-p-762254878", false)]
    public void IsSyntheticUrl_ShouldDetectSyntheticAndAuthenticUrls(string url, bool expectedSynthetic)
    {
        var isSynthetic = EnUcuzTelefon.Shared.Utils.StoreUrlHelper.IsSyntheticUrl(url);
        isSynthetic.Should().Be(expectedSynthetic);
    }

    [Fact]
    public void ResolveSafeStoreUrl_WhenGivenSyntheticUrl_ShouldReturnWorkingStoreSearchUrl()
    {
        var badHbUrl = "https://www.hepsiburada.com/iphone-15-128-gb-p-HBCV921E9E11?magaza=Hepsiburada";
        var safeUrl = EnUcuzTelefon.Shared.Utils.StoreUrlHelper.ResolveSafeStoreUrl("hepsiburada", badHbUrl, "iPhone 15 128GB");

        safeUrl.Should().Be("https://www.hepsiburada.com/ara?q=iPhone%2015%20128GB");
    }

    [Theory]
    [InlineData("https://my-store.vivo.com/tr/iqoo-13-5g", false)]
    [InlineData("https://tr.infinixmobility.com/smartphones", false)]
    [InlineData("https://www.honor.com/tr/phones/", false)]
    [InlineData("https://www.mediamarkt.com.tr/tr/product/_apple-iphone-16-pro-max-256-gb-147054137.html", true)]
    public void IsDirectProductUrl_ShouldRejectGenericBrandAndDeadTemplateUrls(string url, bool expected)
    {
        EnUcuzTelefon.Shared.Utils.StoreUrlHelper.IsDirectProductUrl(url).Should().Be(expected);
    }

    [Theory]
    [InlineData("Nothing Phone (2)", "CMF by Nothing Phone 1 8 GB 256 GB Akıllı Telefon Siyah", false)]
    [InlineData("Nothing Phone (2)", "Nothing Phone (2) 256 GB Beyaz", true)]
    [InlineData("iPhone SE (4. Nesil)", "APPLE iPhone SE 3 GB 64 GB Akıllı Telefon Beyaz", false)]
    [InlineData("Samsung Galaxy S25+", "SAMSUNG Galaxy S25 12 GB 256 GB Akıllı Telefon", false)]
    [InlineData("OPPO Reno 12 Pro 5G", "OPPO Reno 15 Pro 5G 12/512 GB Akıllı Telefon", false)]
    [InlineData("iPhone 16 Pro Max", "APPLE iPhone 16 Pro Max 256 GB Akıllı Telefon (yenilegelsin)", false)]
    [InlineData("Samsung Galaxy S25+", "samsung galaxy s25 12 gb 256 gb samsung turkiye garantili 66614974", false)]
    [InlineData("CMF Phone 1", "nothing phone 1 8 gb 128 gb akilli telefon turuncu 173497740.html", false)]
    public void IsModelMatch_ShouldRejectMismatchedModelsAndTiers(string query, string candidateTitle, bool expected)
    {
        EnUcuzTelefon.Infrastructure.Adapters.MarketAggregator.IsModelMatch(query, candidateTitle).Should().Be(expected);
    }
}
