using System.Text.RegularExpressions;
using EnUcuzTelefon.Application.Interfaces;
using EnUcuzTelefon.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnUcuzTelefon.Infrastructure.Matching;

public class ProductMatchingEngine : IProductMatchingEngine
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<ProductMatchingEngine> _logger;

    public ProductMatchingEngine(IApplicationDbContext context, ILogger<ProductMatchingEngine> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<MatchResult> MatchAsync(string storeTitle, string? storeBrand, decimal price, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storeTitle))
        {
            return new MatchResult(false, null, null, 0, "Empty title");
        }

        var normalizedTitle = NormalizeString(storeTitle);

        // 1. Identify Brand
        var brands = await _context.Brands
            .Include(b => b.Aliases)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        Brand? matchedBrand = null;
        if (!string.IsNullOrWhiteSpace(storeBrand))
        {
            matchedBrand = brands.FirstOrDefault(b =>
                b.Name.Equals(storeBrand, StringComparison.OrdinalIgnoreCase) ||
                b.Aliases.Any(a => a.Alias.Equals(storeBrand, StringComparison.OrdinalIgnoreCase)));
        }

        if (matchedBrand == null)
        {
            matchedBrand = brands.FirstOrDefault(b =>
                normalizedTitle.Contains(NormalizeString(b.Name)) ||
                b.Aliases.Any(a => normalizedTitle.Contains(NormalizeString(a.Alias))));
        }

        if (matchedBrand == null)
        {
            return new MatchResult(false, null, null, 15, "Brand could not be identified");
        }

        // 2. Fetch products for this brand
        var candidateProducts = await _context.Products
            .Include(p => p.Variants)
            .Where(p => p.BrandId == matchedBrand.Id && p.IsActive)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        Product? bestProduct = null;
        int bestProductScore = 0;

        foreach (var product in candidateProducts)
        {
            var pNameNorm = NormalizeString(product.Name);
            var score = CalculateSimilarity(normalizedTitle, pNameNorm);

            // Give high boost if the exact model code or full product name is found
            if (normalizedTitle.Contains(pNameNorm))
            {
                score = Math.Max(score, 85);
            }

            if (!string.IsNullOrEmpty(product.ModelCode) && normalizedTitle.Contains(NormalizeString(product.ModelCode)))
            {
                score = Math.Max(score, 95);
            }

            if (score > bestProductScore)
            {
                bestProductScore = score;
                bestProduct = product;
            }
        }

        if (bestProduct == null || bestProductScore < 60)
        {
            return new MatchResult(false, null, null, bestProductScore, "Product model match confidence too low");
        }

        // 3. Match Variant (Storage & RAM & Color)
        var targetStorage = ExtractStorage(storeTitle);
        var targetRam = ExtractRam(storeTitle);

        ProductVariant? matchedVariant = null;
        if (targetStorage > 0)
        {
            matchedVariant = bestProduct.Variants.FirstOrDefault(v => v.StorageGb == targetStorage);
        }

        if (matchedVariant == null && bestProduct.Variants.Any())
        {
            matchedVariant = bestProduct.Variants.OrderBy(v => v.LowestPrice).First();
        }

        if (matchedVariant == null)
        {
            return new MatchResult(false, bestProduct.Id, null, bestProductScore, "No matching storage variant found");
        }

        // Calculate final confidence score
        int finalScore = bestProductScore;
        if (targetStorage == matchedVariant.StorageGb) finalScore += 10;
        if (targetRam > 0 && targetRam == matchedVariant.RamGb) finalScore += 5;
        finalScore = Math.Min(finalScore, 100);

        return new MatchResult(
            IsMatched: finalScore >= 80,
            ProductId: bestProduct.Id,
            VariantId: matchedVariant.Id,
            ConfidenceScore: finalScore,
            Reason: $"Matched '{bestProduct.Name}' {matchedVariant.StorageGb}GB with score {finalScore}%");
    }

    private static string NormalizeString(string text)
    {
        return Regex.Replace(text.ToLowerInvariant(), @"[^\w\d\s]", " ")
            .Replace("  ", " ")
            .Trim();
    }

    private static int ExtractStorage(string text)
    {
        var match = Regex.Match(text, @"(\d+)\s*(GB|TB)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var val = int.Parse(match.Groups[1].Value);
            var unit = match.Groups[2].Value.ToUpperInvariant();
            return unit == "TB" ? val * 1024 : val;
        }
        return 0;
    }

    private static int ExtractRam(string text)
    {
        var match = Regex.Match(text, @"(\d+)\s*GB\s*RAM", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return int.Parse(match.Groups[1].Value);
        }
        return 0;
    }

    private static int CalculateSimilarity(string source, string target)
    {
        var sourceTokens = source.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var targetTokens = target.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (targetTokens.Length == 0) return 0;

        int matchCount = targetTokens.Count(t => sourceTokens.Contains(t));
        return (int)((double)matchCount / targetTokens.Length * 100);
    }
}
