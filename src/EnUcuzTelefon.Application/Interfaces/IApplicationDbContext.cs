using EnUcuzTelefon.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnUcuzTelefon.Application.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Brand> Brands { get; }
    DbSet<BrandAlias> BrandAliases { get; }
    DbSet<ProductFamily> ProductFamilies { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductVariant> ProductVariants { get; }
    DbSet<ProductSpecification> ProductSpecifications { get; }
    DbSet<ProductImage> ProductImages { get; }
    DbSet<Store> Stores { get; }
    DbSet<Seller> Sellers { get; }
    DbSet<StoreProduct> StoreProducts { get; }
    DbSet<ProductMatch> ProductMatches { get; }
    DbSet<Offer> Offers { get; }
    DbSet<PriceHistory> PriceHistories { get; }
    DbSet<StockStatus> StockStatuses { get; }
    DbSet<PriceAlert> PriceAlerts { get; }
    DbSet<Favorite> Favorites { get; }
    DbSet<ScrapingJob> ScrapingJobs { get; }
    DbSet<JobLog> JobLogs { get; }
    DbSet<PriceAnomaly> PriceAnomalies { get; }
    DbSet<DataValidationLog> DataValidationLogs { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<Article> Articles { get; }
    DbSet<QAItem> QAItems { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
