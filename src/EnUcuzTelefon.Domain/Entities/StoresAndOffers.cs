using EnUcuzTelefon.Domain.Common;
using EnUcuzTelefon.Domain.Enums;
using MatchType = EnUcuzTelefon.Domain.Enums.MatchType;

namespace EnUcuzTelefon.Domain.Entities;

public class Store : BaseEntity, IAggregateRoot
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public StoreType Type { get; set; }
    public string TypeLabel { get; set; } = string.Empty;
    public StoreStatus Status { get; set; } = StoreStatus.Active;
    public IntegrationMethod IntegrationMethod { get; set; }
    public int ProductCount { get; set; }
    public int VerifiedPriceCount { get; set; }
    public DateTime? LastSyncTime { get; set; }
    public DateTime? LastErrorTime { get; set; }
    public int FailedRecordCount { get; set; }
    public bool IsVerified { get; set; } = true;
    public double ReliabilityScore { get; set; } = 9.5;
    public string ShippingInfo { get; set; } = string.Empty;
    public string ReturnPolicy { get; set; } = string.Empty;
    public bool DistributorGuarantee { get; set; } = true;
    public int AllowedRateLimitPerMinute { get; set; } = 60;
    public int SyncIntervalMinutes { get; set; } = 120; // 2 hours default
    public bool IsActive { get; set; } = true;

    public ICollection<Seller> Sellers { get; set; } = new List<Seller>();
    public ICollection<Offer> Offers { get; set; } = new List<Offer>();
    public ICollection<StoreProduct> StoreProducts { get; set; } = new List<StoreProduct>();
    public ICollection<ScrapingJob> ScrapingJobs { get; set; } = new List<ScrapingJob>();
}

public class Seller : BaseEntity
{
    public Guid StoreId { get; set; }
    public Store Store { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public double Rating { get; set; } = 9.5;
    public bool IsOfficial { get; set; }
    public string? ExternalSellerId { get; set; }

    public ICollection<Offer> Offers { get; set; } = new List<Offer>();
}

public class StoreProduct : BaseEntity
{
    public Guid StoreId { get; set; }
    public Store Store { get; set; } = null!;
    public Guid? SellerId { get; set; }
    public Seller? Seller { get; set; }

    public string ExternalProductId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public decimal RawPrice { get; set; }
    public string? RawDataJson { get; set; }
    public DateTime LastCheckedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    public ICollection<ProductMatch> Matches { get; set; } = new List<ProductMatch>();
}

public class ProductMatch : BaseEntity
{
    public Guid StoreProductId { get; set; }
    public StoreProduct StoreProduct { get; set; } = null!;
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public Guid VariantId { get; set; }
    public ProductVariant Variant { get; set; } = null!;

    public int ConfidenceScore { get; set; } // 0-100
    public MatchType MatchType { get; set; }
    public DateTime MatchedAt { get; set; } = DateTime.UtcNow;
    public string? VerifiedByUserId { get; set; }
    public string? Notes { get; set; }
}

public class Offer : BaseEntity, IAggregateRoot
{
    public Guid VariantId { get; set; }
    public ProductVariant Variant { get; set; } = null!;
    public Guid StoreId { get; set; }
    public Store Store { get; set; } = null!;
    public Guid? SellerId { get; set; }
    public Seller? Seller { get; set; }
    public Guid? StoreProductId { get; set; }
    public StoreProduct? StoreProduct { get; set; }

    public decimal Price { get; set; } // Current selling price in TRY
    public decimal? OriginalPrice { get; set; } // Strikethrough/list price
    public decimal ShippingCost { get; set; } = 0;
    public decimal TotalCost { get; set; } // Price + Shipping + mandatory fees
    public string Currency { get; set; } = "TRY";
    public string Url { get; set; } = string.Empty;
    public bool IsAvailable { get; set; } = true;
    public DateTime LastCheckedAt { get; set; } = DateTime.UtcNow;
    public string? ShippingText { get; set; }
    public string? GuaranteeText { get; set; }
    public string? InstallmentText { get; set; }
    public bool IsOfficial { get; set; }
    public bool IsVerified { get; set; } = true;
    public int ConsecutiveFailures { get; set; } = 0;
    public decimal? PendingPrice { get; set; }

    public StockStatus? StockStatus { get; set; }
    public ICollection<PriceHistory> PriceHistories { get; set; } = new List<PriceHistory>();
}

public class PriceHistory : BaseEntity
{
    public Guid VariantId { get; set; }
    public ProductVariant Variant { get; set; } = null!;
    public Guid OfferId { get; set; }
    public Offer Offer { get; set; } = null!;
    public Guid StoreId { get; set; }
    public Store Store { get; set; } = null!;
    public Guid? SellerId { get; set; }
    public Seller? Seller { get; set; }

    public decimal Price { get; set; }
    public decimal PreviousPrice { get; set; }
    public decimal ChangeAmount { get; set; }
    public double ChangePercent { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Source { get; set; } = "AutomatedWorker";
    public bool IsVerified { get; set; } = true;
}

public class StockStatus : BaseEntity
{
    public Guid OfferId { get; set; }
    public Offer Offer { get; set; } = null!;

    public bool IsInStock { get; set; } = true;
    public int? StockCount { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
