using EnUcuzTelefon.Domain.Entities;
using EnUcuzTelefon.Infrastructure.Matching;
using EnUcuzTelefon.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnUcuzTelefon.UnitTests;

public class ProductMatchingEngineTests
{
    private readonly ApplicationDbContext _context;
    private readonly ProductMatchingEngine _sut;

    public ProductMatchingEngineTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _sut = new ProductMatchingEngine(_context, NullLogger<ProductMatchingEngine>.Instance);
    }

    [Fact]
    public async Task WhenTitleHasMatchingBrandAndModelAndStorage_ShouldMatchWithHighConfidence()
    {
        // Arrange
        var brand = new Brand { Name = "Apple", Slug = "apple", Country = "ABD" };
        var product = new Product
        {
            Brand = brand,
            Name = "iPhone 16 Pro Max",
            Slug = "iphone-16-pro-max",
            ModelCode = "A3296",
            IsActive = true
        };
        var variant256 = new ProductVariant
        {
            Product = product,
            Sku = "IP16PM-256",
            RamGb = 8,
            StorageGb = 256,
            Color = "Siyah"
        };
        var variant512 = new ProductVariant
        {
            Product = product,
            Sku = "IP16PM-512",
            RamGb = 8,
            StorageGb = 512,
            Color = "Siyah"
        };

        _context.Brands.Add(brand);
        _context.Products.Add(product);
        _context.ProductVariants.AddRange(variant256, variant512);
        await _context.SaveChangesAsync();

        // Act - Match title: "Apple iPhone 16 Pro Max 256GB Siyah Akıllı Telefon"
        var match = await _sut.MatchAsync(
            storeTitle: "Apple iPhone 16 Pro Max 256GB Siyah Akıllı Telefon",
            storeBrand: "Apple",
            price: 75000m);

        // Assert
        match.IsMatched.Should().BeTrue();
        match.ProductId.Should().Be(product.Id);
        match.VariantId.Should().Be(variant256.Id); // Matched exact 256GB storage!
        match.ConfidenceScore.Should().BeGreaterThanOrEqualTo(80);
    }

    [Fact]
    public async Task WhenTitleBelongsToUnknownBrand_ShouldFailMatch()
    {
        // Arrange (No matching brand in DB)
        var match = await _sut.MatchAsync(
            storeTitle: "SuperPhone XYZ 128GB Black",
            storeBrand: "UnknownBrand",
            price: 15000m);

        // Assert
        match.IsMatched.Should().BeFalse();
        match.ConfidenceScore.Should().BeLessThan(60);
    }
}
