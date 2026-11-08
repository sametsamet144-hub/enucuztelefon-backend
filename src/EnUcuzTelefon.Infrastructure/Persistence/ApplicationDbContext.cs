using System.Text.Json;
using EnUcuzTelefon.Application.Interfaces;
using EnUcuzTelefon.Domain.Entities;
using EnUcuzTelefon.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EnUcuzTelefon.Infrastructure.Persistence;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<BrandAlias> BrandAliases => Set<BrandAlias>();
    public DbSet<ProductFamily> ProductFamilies => Set<ProductFamily>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductSpecification> ProductSpecifications => Set<ProductSpecification>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<Seller> Sellers => Set<Seller>();
    public DbSet<StoreProduct> StoreProducts => Set<StoreProduct>();
    public DbSet<ProductMatch> ProductMatches => Set<ProductMatch>();
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<PriceHistory> PriceHistories => Set<PriceHistory>();
    public DbSet<StockStatus> StockStatuses => Set<StockStatus>();
    public DbSet<PriceAlert> PriceAlerts => Set<PriceAlert>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<ScrapingJob> ScrapingJobs => Set<ScrapingJob>();
    public DbSet<JobLog> JobLogs => Set<JobLog>();
    public DbSet<PriceAnomaly> PriceAnomalies => Set<PriceAnomaly>();
    public DbSet<DataValidationLog> DataValidationLogs => Set<DataValidationLog>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<QAItem> QAItems => Set<QAItem>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // String list JSON value comparer
        var stringListComparer = new ValueComparer<List<string>>(
            (c1, c2) => (c1 ?? new List<string>()).SequenceEqual(c2 ?? new List<string>()),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => c.ToList());

        // Brand
        builder.Entity<Brand>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Slug).IsUnique();
            b.Property(x => x.Name).HasMaxLength(100).IsRequired();
            b.Property(x => x.Slug).HasMaxLength(120).IsRequired();
        });

        // Product
        builder.Entity<Product>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Slug).IsUnique();
            b.HasIndex(x => x.BrandId);
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Slug).HasMaxLength(220).IsRequired();
            b.Property(x => x.Tags).HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
                .Metadata.SetValueComparer(stringListComparer);

            b.HasOne(x => x.Brand)
                .WithMany(x => x.Products)
                .HasForeignKey(x => x.BrandId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(x => x.ProductFamily)
                .WithMany(x => x.Products)
                .HasForeignKey(x => x.ProductFamilyId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ProductVariant
        builder.Entity<ProductVariant>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.ProductId);
            b.HasIndex(x => x.Sku);
            b.Property(x => x.LowestPrice).HasPrecision(18, 2);
            b.Property(x => x.HighestPrice).HasPrecision(18, 2);
            b.Property(x => x.SixMonthLowestPrice).HasPrecision(18, 2);
            b.Property(x => x.PreviousLowestPrice).HasPrecision(18, 2);

            b.HasOne(x => x.Product)
                .WithMany(x => x.Variants)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Store
        builder.Entity<Store>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Slug).IsUnique();
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Slug).HasMaxLength(150).IsRequired();
        });

        // Seller
        builder.Entity<Seller>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.StoreId, x.Slug });
            b.HasOne(x => x.Store)
                .WithMany(x => x.Sellers)
                .HasForeignKey(x => x.StoreId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Offer
        builder.Entity<Offer>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.VariantId);
            b.HasIndex(x => x.StoreId);
            b.Property(x => x.Price).HasPrecision(18, 2);
            b.Property(x => x.OriginalPrice).HasPrecision(18, 2);
            b.Property(x => x.PendingPrice).HasPrecision(18, 2);
            b.Property(x => x.ShippingCost).HasPrecision(18, 2);
            b.Property(x => x.TotalCost).HasPrecision(18, 2);

            b.HasOne(x => x.Variant)
                .WithMany(x => x.Offers)
                .HasForeignKey(x => x.VariantId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(x => x.Store)
                .WithMany(x => x.Offers)
                .HasForeignKey(x => x.StoreId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(x => x.Seller)
                .WithMany(x => x.Offers)
                .HasForeignKey(x => x.SellerId)
                .OnDelete(DeleteBehavior.SetNull);

            b.HasOne(x => x.StockStatus)
                .WithOne(x => x.Offer)
                .HasForeignKey<StockStatus>(x => x.OfferId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // PriceHistory
        builder.Entity<PriceHistory>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.VariantId, x.Timestamp });
            b.HasIndex(x => x.OfferId);
            b.Property(x => x.Price).HasPrecision(18, 2);
            b.Property(x => x.PreviousPrice).HasPrecision(18, 2);
            b.Property(x => x.ChangeAmount).HasPrecision(18, 2);

            b.HasOne(x => x.Variant)
                .WithMany(x => x.PriceHistories)
                .HasForeignKey(x => x.VariantId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(x => x.Offer)
                .WithMany(x => x.PriceHistories)
                .HasForeignKey(x => x.OfferId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // PriceAlert
        builder.Entity<PriceAlert>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.VariantId, x.IsActive });
            b.Property(x => x.TargetPrice).HasPrecision(18, 2);
            b.Property(x => x.TriggeredPrice).HasPrecision(18, 2);
        });

        // PriceAnomaly
        builder.Entity<PriceAnomaly>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.OldPrice).HasPrecision(18, 2);
            b.Property(x => x.NewPrice).HasPrecision(18, 2);
        });

        // Article & QAItem
        builder.Entity<Article>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Slug).IsUnique();
            b.Property(x => x.Tags).HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
                .Metadata.SetValueComparer(stringListComparer);
        });

        builder.Entity<QAItem>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Slug).IsUnique();
            b.Property(x => x.Tags).HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
                .Metadata.SetValueComparer(stringListComparer);
        });
    }
}
