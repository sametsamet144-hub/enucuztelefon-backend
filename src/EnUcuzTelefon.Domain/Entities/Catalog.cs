using EnUcuzTelefon.Domain.Common;
using EnUcuzTelefon.Domain.Enums;

namespace EnUcuzTelefon.Domain.Entities;

public class Brand : BaseEntity, IAggregateRoot
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string LogoUrl { get; set; } = string.Empty;
    public int PhoneCount { get; set; }
    public string Country { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Website { get; set; }
    public int? FoundedYear { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<BrandAlias> Aliases { get; set; } = new List<BrandAlias>();
    public ICollection<ProductFamily> Families { get; set; } = new List<ProductFamily>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
}

public class BrandAlias : BaseEntity
{
    public Guid BrandId { get; set; }
    public Brand Brand { get; set; } = null!;
    public string Alias { get; set; } = string.Empty;
}

public class ProductFamily : BaseEntity
{
    public Guid BrandId { get; set; }
    public Brand Brand { get; set; } = null!;
    public string Name { get; set; } = string.Empty; // e.g. "iPhone", "Galaxy S"
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<Product> Products { get; set; } = new List<Product>();
}

public class Product : BaseEntity, IAggregateRoot
{
    public Guid BrandId { get; set; }
    public Brand Brand { get; set; } = null!;
    public Guid? ProductFamilyId { get; set; }
    public ProductFamily? ProductFamily { get; set; }

    public string Name { get; set; } = string.Empty; // e.g. "iPhone 16 Pro Max"
    public string Slug { get; set; } = string.Empty; // e.g. "iphone-16-pro-max"
    public string? ModelCode { get; set; } // e.g. "A3296"
    public DateTime? ReleaseDate { get; set; }
    public bool IsNew { get; set; }
    public double Rating { get; set; }
    public int ReviewCount { get; set; }
    public string? Description { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public bool IsActive { get; set; } = true;

    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
    public ICollection<ProductSpecification> Specifications { get; set; } = new List<ProductSpecification>();
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
    public ICollection<ProductMatch> Matches { get; set; } = new List<ProductMatch>();
}

public class ProductVariant : BaseEntity, IAggregateRoot
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string Sku { get; set; } = string.Empty;
    public int RamGb { get; set; }
    public int StorageGb { get; set; }
    public string Color { get; set; } = string.Empty;
    public ProductCondition Condition { get; set; } = ProductCondition.New;
    public string? ModelNumber { get; set; }
    public string? Region { get; set; }
    public string? SimType { get; set; }
    public string? WarrantyType { get; set; }
    public string? Barcode { get; set; } // EAN / UPC

    // Cached denormalized price calculations (updated on price change)
    public decimal LowestPrice { get; set; }
    public decimal HighestPrice { get; set; }
    public decimal? SixMonthLowestPrice { get; set; }
    public decimal? PreviousLowestPrice { get; set; }
    public int PriceDropPercent { get; set; }
    public bool IsPriceDropped { get; set; }
    public DateTime? PriceDropDetectedAt { get; set; }
    public string? LowestPriceStoreName { get; set; }
    public string? LowestPriceUrl { get; set; }

    public ICollection<Offer> Offers { get; set; } = new List<Offer>();
    public ICollection<PriceHistory> PriceHistories { get; set; } = new List<PriceHistory>();
    public ICollection<PriceAlert> PriceAlerts { get; set; } = new List<PriceAlert>();
}

public class ProductSpecification : BaseEntity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public Guid? VariantId { get; set; }
    public ProductVariant? Variant { get; set; }

    public string Category { get; set; } = string.Empty; // e.g. "display", "camera", "battery"
    public string Key { get; set; } = string.Empty; // e.g. "size", "chipset", "capacity"
    public string Value { get; set; } = string.Empty;
    public string? Unit { get; set; } // e.g. "inches", "mAh", "W", "MP"
    public string? Source { get; set; } // e.g. "Official Spec Sheet", "GSMArena"
    public string? SourceUrl { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public int ConfidenceLevel { get; set; } = 100; // 0-100
}

public class ProductImage : BaseEntity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public Guid? VariantId { get; set; }
    public ProductVariant? Variant { get; set; }

    public string Url { get; set; } = string.Empty;
    public string? Color { get; set; }
    public string ImageType { get; set; } = "front"; // front, back, side, angle
    public int SortOrder { get; set; }
    public bool IsPrimary { get; set; }
    public string? Source { get; set; }
}
