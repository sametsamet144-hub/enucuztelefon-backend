using EnUcuzTelefon.Application.Interfaces;
using EnUcuzTelefon.Infrastructure.Adapters;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnUcuzTelefon.UnitTests;

public class StoreAdapterTests
{
    private readonly TrendyolAdapter _sut;

    public StoreAdapterTests()
    {
        _sut = new TrendyolAdapter(NullLogger<TrendyolAdapter>.Instance, new HttpClient());
    }

    [Fact]
    public void ValidateProductUrl_WithValidHttpsUrl_ShouldReturnTrue()
    {
        var isValid = _sut.ValidateProductUrl("https://www.trendyol.com.tr/urun/iphone-16-pro-max");
        isValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateProductUrl_WithInvalidUrl_ShouldReturnFalse()
    {
        var isValid = _sut.ValidateProductUrl("not-a-valid-url");
        isValid.Should().BeFalse();
    }

    [Fact]
    public void NormalizeProduct_ShouldExtractBrandModelAndStorageCorrectly()
    {
        var raw = new StoreRawProduct(
            ExternalId: "TEST-01",
            Title: "Apple iPhone 16 Pro Max 256GB Siyah",
            Url: "https://trendyol.com/test",
            Price: 75000m,
            SellerName: "Official Seller",
            InStock: true,
            ImageUrl: null,
            Brand: null,
            Model: null,
            RamGb: null,
            StorageGb: null,
            Color: null,
            RawJson: null);

        var normalized = _sut.NormalizeProduct(raw);

        normalized.BrandName.Should().Be("Apple");
        normalized.StorageGb.Should().Be(256);
        normalized.Color.Should().Be("Siyah");
        normalized.Price.Should().Be(75000m);
        normalized.ShippingCost.Should().Be(0m); // Price > 500 => free shipping
        normalized.TotalCost.Should().Be(75000m);
    }
}
