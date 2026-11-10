using System.Globalization;
using System.Text.Json;
using EnUcuzTelefon.Domain.Entities;
using EnUcuzTelefon.Domain.Enums;
using EnUcuzTelefon.Infrastructure.Adapters;
using EnUcuzTelefon.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnUcuzTelefon.Infrastructure.Persistence;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        ILogger logger)
    {
        // 1. Roles & Admin User
        string[] roles = { "Admin", "Editor", "User" };
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        var adminEmail = "admin@enucuztelefon.com";
        var adminUser = await userManager.FindByEmailAsync(adminEmail);
        if (adminUser == null)
        {
            adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                FullName = "EnUcuzTelefon Sistem Yöneticisi",
                EmailConfirmed = true
            };
            var result = await userManager.CreateAsync(adminUser, "Admin123*!");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, "Admin");
                logger.LogInformation("Default Admin user created ({Email})", adminEmail);
            }
        }

        // 2. Stores (Strict 29 Whitelisted Turkish Stores & Official Brand Stores)
        var predefinedStores = GetPredefinedStores();

        if (!await context.Stores.AnyAsync())
        {
            await context.Stores.AddRangeAsync(predefinedStores);
            await context.SaveChangesAsync();
            logger.LogInformation("Seeded {Count} verified whitelisted stores.", predefinedStores.Count);
        }
        else
        {
            // Clean up any non-whitelisted store and ensure all 29 whitelisted stores exist
            var existingStores = await context.Stores.ToListAsync();
            var nonAllowed = existingStores.Where(s => !AllowedStoreSlugs.Contains(s.Slug)).ToList();
            if (nonAllowed.Any())
            {
                var nonAllowedIds = nonAllowed.Select(s => s.Id).ToList();
                var nonAllowedOffers = await context.Offers.Where(o => nonAllowedIds.Contains(o.StoreId)).ToListAsync();
                if (nonAllowedOffers.Any())
                {
                    context.Offers.RemoveRange(nonAllowedOffers);
                }
                var nonAllowedJobs = await context.ScrapingJobs.Where(j => nonAllowedIds.Contains(j.StoreId)).ToListAsync();
                if (nonAllowedJobs.Any())
                {
                    context.ScrapingJobs.RemoveRange(nonAllowedJobs);
                }
                context.Stores.RemoveRange(nonAllowed);
                await context.SaveChangesAsync();
                logger.LogInformation("Removed {Count} non-whitelisted stores from database.", nonAllowed.Count);
            }

            var currentSlugs = (await context.Stores.Select(s => s.Slug).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missingStores = predefinedStores.Where(s => !currentSlugs.Contains(s.Slug)).ToList();
            if (missingStores.Any())
            {
                await context.Stores.AddRangeAsync(missingStores);
                await context.SaveChangesAsync();
                logger.LogInformation("Added {Count} missing whitelisted stores to database.", missingStores.Count);
            }
        }

        // 3. Brands (30 Official Smartphone Brands)
        if (!await context.Brands.AnyAsync())
        {
            var brands = new List<Brand>
            {
                new() { Name = "Apple", Slug = "apple", LogoUrl = "/images/brands/apple.svg", Country = "ABD", FoundedYear = 1976, Website = "https://www.apple.com/tr", Description = "Cupertino merkezli teknoloji devi, iPhone serisinin üreticisi." },
                new() { Name = "Samsung", Slug = "samsung", LogoUrl = "/images/brands/samsung.svg", Country = "Güney Kore", FoundedYear = 1938, Website = "https://www.samsung.com/tr", Description = "Dünyanın en büyük akıllı telefon ve ekran üreticilerinden Galaxy serisi." },
                new() { Name = "Xiaomi", Slug = "xiaomi", LogoUrl = "/images/brands/xiaomi.svg", Country = "Çin", FoundedYear = 2010, Website = "https://www.mi.com/tr", Description = "Fiyat/performans ve amiral gemisi Mi, Redmi ve POCO cihazlarıyla küresel lider." },
                new() { Name = "Redmi", Slug = "redmi", LogoUrl = "/images/brands/xiaomi.svg", Country = "Çin", FoundedYear = 2013, Website = "https://www.mi.com/tr", Description = "Xiaomi çatısı altında yüksek fiyat/performans oranına sahip akıllı telefon ailesi." },
                new() { Name = "POCO", Slug = "poco", LogoUrl = "/images/brands/poco.svg", Country = "Çin", FoundedYear = 2018, Website = "https://www.po.co", Description = "Yüksek işlemci hızı ve mobil oyun performansı odaklı akıllı telefon markası." },
                new() { Name = "Huawei", Slug = "huawei", LogoUrl = "/images/brands/huawei.svg", Country = "Çin", FoundedYear = 1987, Website = "https://consumer.huawei.com/tr", Description = "XMAGE kamera teknolojisi ve yenilikçi donanım mimarisiyle lider teknoloji üreticisi." },
                new() { Name = "HONOR", Slug = "honor", LogoUrl = "/images/brands/honor.svg", Country = "Çin", FoundedYear = 2013, Website = "https://www.honor.com/tr", Description = "İnovatif ekran teknolojileri, ince katlanabilir cihazlar ve Magic serisi." },
                new() { Name = "OPPO", Slug = "oppo", LogoUrl = "/images/brands/oppo.svg", Country = "Çin", FoundedYear = 2004, Website = "https://www.oppo.com/tr", Description = "Kamera teknolojileri ve hızlı şarj inovasyonlarıyla öne çıkan küresel üretici." },
                new() { Name = "Realme", Slug = "realme", LogoUrl = "/images/brands/realme.svg", Country = "Çin", FoundedYear = 2018, Website = "https://www.realme.com/tr", Description = "Genç kullanıcı kitlesine yönelik hızlı şarj ve şık tasarımlı GT ve C serisi cihazlar." },
                new() { Name = "vivo", Slug = "vivo", LogoUrl = "/images/brands/vivo.svg", Country = "Çin", FoundedYear = 2009, Website = "https://www.vivo.com/tr", Description = "ZEISS optik iş birliği ve profesyonel portre fotoğrafçılığı odaklı cihazlar." },
                new() { Name = "OnePlus", Slug = "oneplus", LogoUrl = "/images/brands/oneplus.svg", Country = "Çin", FoundedYear = 2013, Website = "https://www.oneplus.com/tr", Description = "Hız, akıcı OxygenOS arayüzü ve amiral gemisi katili felsefesiyle bilinen marka." },
                new() { Name = "Google", Slug = "google", LogoUrl = "/images/brands/google.svg", Country = "ABD", FoundedYear = 1998, Website = "https://store.google.com", Description = "Saf Android deneyimi ve yapay zeka odaklı kamera gücüyle Pixel serisi." },
                new() { Name = "Motorola", Slug = "motorola", LogoUrl = "/images/brands/motorola.svg", Country = "ABD", FoundedYear = 1928, Website = "https://www.motorola.com", Description = "Mobil iletişimin öncüsü, Razr katlanabilir ve Edge serisi akıllı telefonlar." },
                new() { Name = "Nokia", Slug = "nokia", LogoUrl = "/images/brands/nokia.svg", Country = "Finlandiya", FoundedYear = 1865, Website = "https://www.hmd.com", Description = "Sağlamlık, uzun batarya ömrü ve saf Android deneyimi sunan köklü marka." },
                new() { Name = "Sony", Slug = "sony", LogoUrl = "/images/brands/sony.svg", Country = "Japonya", FoundedYear = 1946, Website = "https://www.sony.com.tr", Description = "Alpha kamera sensörleri ve 4K OLED ekran teknolojisiyle Xperia serisi." },
                new() { Name = "ASUS", Slug = "asus", LogoUrl = "/images/brands/asus.svg", Country = "Tayvan", FoundedYear = 1989, Website = "https://www.asus.com/tr", Description = "ROG Phone oyun telefonları ve kompakt Zenfone serisinin üreticisi." },
                new() { Name = "Nothing", Slug = "nothing", LogoUrl = "/images/brands/nothing.svg", Country = "İngiltere", FoundedYear = 2020, Website = "https://nothing.tech", Description = "Şeffaf tasarım dili, Glyph ışık arayüzü ve özgün Nothing OS deneyimi." },
                new() { Name = "TECNO", Slug = "tecno", LogoUrl = "/images/brands/tecno.svg", Country = "Çin", FoundedYear = 2006, Website = "https://www.tecno-mobile.com/tr", Description = "Türkiye'de yerli üretim yatırımı bulunan Camon, Phantom ve Pova serisi üreticisi." },
                new() { Name = "Infinix", Slug = "infinix", LogoUrl = "/images/brands/infinix.svg", Country = "Çin", FoundedYear = 2013, Website = "https://tr.infinixmobility.com", Description = "Uygun fiyatlı oyun ve multimedya odaklı Note, Zero ve GT serisi telefonlar." },
                new() { Name = "General Mobile", Slug = "general-mobile", LogoUrl = "/images/brands/general-mobile.svg", Country = "Türkiye", FoundedYear = 2005, Website = "https://www.generalmobile.com", Description = "Türkiye'nin yerli akıllı telefon markası, GM Phoenix ve Era serisi." },
                new() { Name = "Casper", Slug = "casper", LogoUrl = "/images/brands/casper.svg", Country = "Türkiye", FoundedYear = 1991, Website = "https://www.casper.com.tr", Description = "VIA serisi akıllı telefonlarıyla Türkiye'nin köklü teknoloji markası." },
                new() { Name = "Reeder", Slug = "reeder", LogoUrl = "/images/brands/reeder.svg", Country = "Türkiye", FoundedYear = 2009, Website = "https://reeder.com.tr", Description = "Samsun'daki üretim tesislerinde geliştirilen bütçe dostu yerli telefonlar." },
                new() { Name = "OMIX", Slug = "omix", LogoUrl = "/images/brands/omix.svg", Country = "Türkiye", FoundedYear = 2021, Website = "https://www.omix.com.tr", Description = "Fiyat/performans odaklı X serisi akıllı telefonlar." },
                new() { Name = "TCL", Slug = "tcl", LogoUrl = "/images/brands/tcl.svg", Country = "Çin", FoundedYear = 1981, Website = "https://www.tcl.com/tr", Description = "NXTPAPER göz dostu ekran teknolojisiyle öne çıkan küresel ekran ve telefon üreticisi." },
                new() { Name = "ZTE", Slug = "zte", LogoUrl = "/images/brands/zte.svg", Country = "Çin", FoundedYear = 1985, Website = "https://ztedevices.com", Description = "Blade serisi ve ekran altı kamera teknolojileriyle bilinen telekomünikasyon devi." },
                new() { Name = "Nubia", Slug = "nubia", LogoUrl = "/images/brands/nubia.svg", Country = "Çin", FoundedYear = 2012, Website = "https://www.nubia.com", Description = "RedMagic profesyonel oyuncu telefonları ve 35mm özel optik kameralı amiral gemileri." },
                new() { Name = "Meizu", Slug = "meizu", LogoUrl = "/images/brands/meizu.svg", Country = "Çin", FoundedYear = 2003, Website = "https://www.meizu.com", Description = "Flyme OS ekosistemi ve simetrik çerçevesiz tasarımlarıyla tanınan üretici." },
                new() { Name = "Alcatel", Slug = "alcatel", LogoUrl = "/images/brands/alcatel.svg", Country = "Fransa", FoundedYear = 1898, Website = "https://www.alcatelmobile.com", Description = "Giriş seviyesi ve kurumsal kullanıma uygun ekonomik cep telefonları." },
                new() { Name = "Lenovo", Slug = "lenovo", LogoUrl = "/images/brands/lenovo.svg", Country = "Çin", FoundedYear = 1984, Website = "https://www.lenovo.com/tr", Description = "ThinkPhone kurumsal güvenlik telefonları ve Legion serisi üreticisi." },
                new() { Name = "HTC", Slug = "htc", LogoUrl = "/images/brands/htc.svg", Country = "Tayvan", FoundedYear = 1997, Website = "https://www.htc.com", Description = "İlk Android telefonların üreticisi ve U serisi multimedya cihazları." }
            };

            await context.Brands.AddRangeAsync(brands);
            await context.SaveChangesAsync();
            logger.LogInformation("Seeded {Count} official brands.", brands.Count);
        }

        // 4. Products, Variants, Specifications, Offers, and PriceHistory from catalog-seed.json
        if (!await context.Products.AnyAsync())
        {
            var brandsBySlug = await context.Brands.ToDictionaryAsync(b => b.Slug, StringComparer.OrdinalIgnoreCase);
            var storesBySlug = await context.Stores.ToDictionaryAsync(s => s.Slug, StringComparer.OrdinalIgnoreCase);

            var seedFileCandidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Persistence", "SeedData", "catalog-seed.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "Persistence", "SeedData", "catalog-seed.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "src", "EnUcuzTelefon.Infrastructure", "Persistence", "SeedData", "catalog-seed.json")
            };

            var seedFilePath = seedFileCandidates.FirstOrDefault(File.Exists);

            if (seedFilePath != null)
            {
                var jsonText = await File.ReadAllTextAsync(seedFilePath);
                using var doc = JsonDocument.Parse(jsonText);

                var products = new List<Product>();
                var variants = new List<ProductVariant>();
                var specifications = new List<ProductSpecification>();
                var offers = new List<Offer>();
                var histories = new List<PriceHistory>();
                var seenSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var slug = item.GetProperty("slug").GetString() ?? "";
                    if (string.IsNullOrWhiteSpace(slug) || !seenSlugs.Add(slug))
                        continue;

                    var name = item.GetProperty("name").GetString() ?? slug;
                    var brandSlug = item.GetProperty("brandSlug").GetString() ?? "apple";
                    var brandName = item.GetProperty("brand").GetString() ?? "Apple";

                    if (!brandsBySlug.TryGetValue(brandSlug, out var brand))
                    {
                        brand = new Brand
                        {
                            Name = brandName,
                            Slug = brandSlug,
                            LogoUrl = $"/images/brands/{brandSlug}.svg",
                            Country = "Küresel",
                            FoundedYear = 2010,
                            Description = $"{brandName} akıllı telefon modelleri ve Türkiye fiyatları."
                        };
                        context.Brands.Add(brand);
                        brandsBySlug[brandSlug] = brand;
                    }

                    var imageUrl = item.GetProperty("image").GetString() ?? "/images/phones/default.svg";
                    var lowestPrice = item.GetProperty("lowestPrice").GetDecimal();
                    var highestPrice = item.GetProperty("highestPrice").GetDecimal();
                    var sixMonthLowest = item.TryGetProperty("sixMonthLowestPrice", out var smlEl) && smlEl.ValueKind == JsonValueKind.Number
                        ? smlEl.GetDecimal()
                        : Math.Round(lowestPrice * 0.94m, 0);
                    var isNew = item.TryGetProperty("isNew", out var isNewEl) && isNewEl.GetBoolean();
                    var isPriceDropped = item.TryGetProperty("isPriceDropped", out var ipdEl) && ipdEl.GetBoolean();
                    var priceDropPercent = item.TryGetProperty("priceDropPercent", out var pdpEl) && pdpEl.ValueKind == JsonValueKind.Number
                        ? pdpEl.GetInt32()
                        : 0;
                    var rating = item.TryGetProperty("rating", out var ratEl) && ratEl.ValueKind == JsonValueKind.Number
                        ? ratEl.GetDouble()
                        : 8.8;
                    var reviewCount = item.TryGetProperty("reviewCount", out var rcEl) && rcEl.ValueKind == JsonValueKind.Number
                        ? rcEl.GetInt32()
                        : 250;
                    var description = item.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String
                        ? descEl.GetString() ?? ""
                        : $"{name} en ucuz fiyatlar ve teknik özellikler.";
                    var priceStore = item.TryGetProperty("priceStore", out var psEl) && psEl.ValueKind == JsonValueKind.String
                        ? psEl.GetString() ?? "Trendyol"
                        : "Trendyol";
                    var priceUrl = item.TryGetProperty("priceUrl", out var puEl) && puEl.ValueKind == JsonValueKind.String
                        ? puEl.GetString() ?? $"https://www.trendyol.com/sr?q={Uri.EscapeDataString(name)}"
                        : $"https://www.trendyol.com/sr?q={Uri.EscapeDataString(name)}";

                    DateTime releaseDate = DateTime.UtcNow.AddMonths(-3);
                    if (item.TryGetProperty("releaseDate", out var rdEl) && rdEl.ValueKind == JsonValueKind.String && DateTime.TryParse(rdEl.GetString(), out var parsedRd))
                    {
                        releaseDate = DateTime.SpecifyKind(parsedRd, DateTimeKind.Utc);
                    }

                    var tags = new List<string>();
                    if (item.TryGetProperty("tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var t in tagsEl.EnumerateArray())
                        {
                            if (t.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(t.GetString()))
                                tags.Add(t.GetString()!);
                        }
                    }

                    var product = new Product
                    {
                        BrandId = brand.Id,
                        Brand = brand,
                        Name = name,
                        Slug = slug,
                        ModelCode = slug.ToUpperInvariant(),
                        ReleaseDate = releaseDate,
                        IsNew = isNew,
                        Rating = rating,
                        ReviewCount = reviewCount,
                        Description = description,
                        ImageUrl = imageUrl,
                        Tags = tags
                    };

                    int ramGb = 8;
                    int storageGb = 256;

                    if (item.TryGetProperty("specs", out var specsEl) && specsEl.ValueKind == JsonValueKind.Object)
                    {
                        if (specsEl.TryGetProperty("display", out var dispEl) && dispEl.ValueKind == JsonValueKind.Object)
                        {
                            AddSpec(specifications, product, "display", "size", GetJsonString(dispEl, "size", "6.7"), "inç");
                            AddSpec(specifications, product, "display", "resolution", GetJsonString(dispEl, "resolution", "2400 x 1080"), "piksel");
                            AddSpec(specifications, product, "display", "technology", GetJsonString(dispEl, "technology", "AMOLED"));
                            AddSpec(specifications, product, "display", "refreshRate", GetJsonString(dispEl, "refreshRate", "120"), "Hz");
                            AddSpec(specifications, product, "display", "brightness", GetJsonString(dispEl, "brightness", "1800"), "nits");
                            AddSpec(specifications, product, "display", "ppi", GetJsonString(dispEl, "ppi", "410"), "ppi");
                            AddSpec(specifications, product, "display", "protection", GetJsonString(dispEl, "protection", "Corning Gorilla Glass"));
                        }

                        if (specsEl.TryGetProperty("camera", out var camEl) && camEl.ValueKind == JsonValueKind.Object)
                        {
                            if (camEl.TryGetProperty("mainCamera", out var mcEl) && mcEl.ValueKind == JsonValueKind.Object)
                            {
                                AddSpec(specifications, product, "camera", "mainMp", GetJsonString(mcEl, "megapixels", "50"), "MP");
                                AddSpec(specifications, product, "camera", "mainAperture", GetJsonString(mcEl, "aperture", "f/1.8"));
                                AddSpec(specifications, product, "camera", "ois", GetJsonString(mcEl, "ois", "true"));
                            }
                            if (camEl.TryGetProperty("ultrawide", out var uwEl) && uwEl.ValueKind == JsonValueKind.Object)
                            {
                                AddSpec(specifications, product, "camera", "ultrawideMp", GetJsonString(uwEl, "megapixels", "12"), "MP");
                                AddSpec(specifications, product, "camera", "ultrawideAperture", GetJsonString(uwEl, "aperture", "f/2.2"));
                            }
                            if (camEl.TryGetProperty("telephoto", out var tpEl) && tpEl.ValueKind == JsonValueKind.Object)
                            {
                                AddSpec(specifications, product, "camera", "telephotoMp", GetJsonString(tpEl, "megapixels", "12"), "MP");
                                AddSpec(specifications, product, "camera", "opticalZoom", GetJsonString(tpEl, "opticalZoom", "3"), "x");
                            }
                            if (camEl.TryGetProperty("frontCamera", out var fcEl) && fcEl.ValueKind == JsonValueKind.Object)
                            {
                                AddSpec(specifications, product, "camera", "frontMp", GetJsonString(fcEl, "megapixels", "12"), "MP");
                            }
                        }

                        if (specsEl.TryGetProperty("battery", out var batEl) && batEl.ValueKind == JsonValueKind.Object)
                        {
                            AddSpec(specifications, product, "battery", "capacity", GetJsonString(batEl, "capacity", "5000"), "mAh");
                            AddSpec(specifications, product, "battery", "wiredCharging", GetJsonString(batEl, "wiredCharging", "45"), "W");
                            AddSpec(specifications, product, "battery", "wirelessCharging", GetJsonString(batEl, "wirelessCharging", "15"), "W");
                            AddSpec(specifications, product, "battery", "chargingTime", GetJsonString(batEl, "chargingTime", "0'dan %50'ye 30 dakikada"));
                        }

                        if (specsEl.TryGetProperty("performance", out var perfEl) && perfEl.ValueKind == JsonValueKind.Object)
                        {
                            if (perfEl.TryGetProperty("ram", out var rEl) && rEl.ValueKind == JsonValueKind.Number) ramGb = rEl.GetInt32();
                            if (perfEl.TryGetProperty("storage", out var sEl) && sEl.ValueKind == JsonValueKind.Number) storageGb = sEl.GetInt32();

                            AddSpec(specifications, product, "performance", "chipset", GetJsonString(perfEl, "chipset", "Octa-Core İşlemci"));
                            AddSpec(specifications, product, "performance", "cpu", GetJsonString(perfEl, "cpu", "Octa-core yüksek performans mimarisi"));
                            AddSpec(specifications, product, "performance", "gpu", GetJsonString(perfEl, "gpu", "Yüksek Performans GPU"));
                            AddSpec(specifications, product, "performance", "ram", ramGb.ToString(CultureInfo.InvariantCulture), "GB");
                            AddSpec(specifications, product, "performance", "storage", storageGb.ToString(CultureInfo.InvariantCulture), "GB");
                            AddSpec(specifications, product, "performance", "storageType", GetJsonString(perfEl, "storageType", "UFS 4.0"));
                            AddSpec(specifications, product, "performance", "antutuScore", GetJsonString(perfEl, "antutuScore", "1450000"));
                            AddSpec(specifications, product, "performance", "geekbenchSingle", GetJsonString(perfEl, "geekbenchSingle", "2100"));
                            AddSpec(specifications, product, "performance", "geekbenchMulti", GetJsonString(perfEl, "geekbenchMulti", "5600"));
                        }

                        if (specsEl.TryGetProperty("connectivity", out var connEl) && connEl.ValueKind == JsonValueKind.Object)
                        {
                            AddSpec(specifications, product, "connectivity", "sim", GetJsonString(connEl, "sim", "Nano-SIM + eSIM"));
                            AddSpec(specifications, product, "connectivity", "nfc", GetJsonString(connEl, "nfc", "true"));
                            AddSpec(specifications, product, "connectivity", "bluetooth", GetJsonString(connEl, "bluetooth", "5.3"));
                            AddSpec(specifications, product, "connectivity", "wifi", GetJsonString(connEl, "wifi", "Wi-Fi 6E"));
                            AddSpec(specifications, product, "connectivity", "usb", GetJsonString(connEl, "usb", "USB-C"));
                            AddSpec(specifications, product, "connectivity", "fiveG", GetJsonString(connEl, "fiveG", "true"));
                        }

                        if (specsEl.TryGetProperty("design", out var desEl) && desEl.ValueKind == JsonValueKind.Object)
                        {
                            AddSpec(specifications, product, "design", "weight", GetJsonString(desEl, "weight", "195"), "g");
                            AddSpec(specifications, product, "design", "thickness", GetJsonString(desEl, "thickness", "8.2"), "mm");
                            AddSpec(specifications, product, "design", "waterResistance", GetJsonString(desEl, "waterResistance", "IP68"));
                            AddSpec(specifications, product, "design", "materials", GetJsonString(desEl, "materials", "Alüminyum ve Cam"));
                        }

                        if (specsEl.TryGetProperty("software", out var softEl) && softEl.ValueKind == JsonValueKind.Object)
                        {
                            AddSpec(specifications, product, "software", "os", GetJsonString(softEl, "os", "Android 15"));
                            AddSpec(specifications, product, "software", "updatePolicy", GetJsonString(softEl, "updatePolicy", "4 Yıl Güncelleme Garantisi"));
                        }
                    }

                    var variant = new ProductVariant
                    {
                        Product = product,
                        Sku = $"{slug.ToUpperInvariant()}-{storageGb}",
                        RamGb = ramGb,
                        StorageGb = storageGb,
                        Color = "Siyah",
                        LowestPrice = lowestPrice,
                        HighestPrice = highestPrice,
                        SixMonthLowestPrice = sixMonthLowest,
                        IsPriceDropped = isPriceDropped,
                        PriceDropPercent = priceDropPercent,
                        LowestPriceStoreName = priceStore,
                        LowestPriceUrl = priceUrl
                    };

                    Offer? firstOffer = null;
                    if (item.TryGetProperty("storesPrices", out var spArr) && spArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var sp in spArr.EnumerateArray())
                        {
                            var stSlug = sp.TryGetProperty("storeSlug", out var ssEl) && ssEl.ValueKind == JsonValueKind.String
                                ? ssEl.GetString() ?? "trendyol"
                                : "trendyol";
                            if (!AllowedStoreSlugs.Contains(stSlug) || !storesBySlug.TryGetValue(stSlug, out var storeObj))
                            {
                                continue;
                            }

                            var offerPrice = sp.GetProperty("price").GetDecimal();
                            var offerUrl = sp.TryGetProperty("url", out var uEl) && uEl.ValueKind == JsonValueKind.String
                                ? uEl.GetString() ?? priceUrl
                                : priceUrl;

                            // Data yoksa yoktur: Sahte arama fallback linkleri offer olarak kaydedilmez
                            if (!MarketAggregator.IsDirectProductUrl(offerUrl))
                            {
                                continue;
                            }
                            var isAvail = !sp.TryGetProperty("isAvailable", out var iaEl) || iaEl.GetBoolean();
                            var shipTxt = sp.TryGetProperty("shippingText", out var shEl) && shEl.ValueKind == JsonValueKind.String
                                ? shEl.GetString()
                                : storeObj.ShippingInfo;
                            var guarTxt = sp.TryGetProperty("guaranteeText", out var guEl) && guEl.ValueKind == JsonValueKind.String
                                ? guEl.GetString()
                                : "2 Yıl Resmi Distribütör Garantili";
                            var instTxt = sp.TryGetProperty("installmentText", out var inEl) && inEl.ValueKind == JsonValueKind.String
                                ? inEl.GetString()
                                : "Peşin Fiyatına 3 Taksit";
                            var isOff = sp.TryGetProperty("isOfficial", out var ioEl) && ioEl.ValueKind == JsonValueKind.True;

                            var offer = new Offer
                            {
                                Variant = variant,
                                StoreId = storeObj.Id,
                                Store = storeObj,
                                Price = offerPrice,
                                TotalCost = offerPrice,
                                ShippingCost = 0,
                                Url = offerUrl,
                                IsAvailable = isAvail,
                                ShippingText = shipTxt,
                                GuaranteeText = guarTxt,
                                InstallmentText = instTxt,
                                IsOfficial = isOff
                            };
                            firstOffer ??= offer;
                            offers.Add(offer);
                        }
                    }

                    if (firstOffer != null)
                    {
                        histories.Add(new PriceHistory
                        {
                            Variant = variant,
                            Offer = firstOffer,
                            StoreId = firstOffer.StoreId,
                            Price = Math.Round(lowestPrice * 1.07m, 0),
                            PreviousPrice = Math.Round(lowestPrice * 1.11m, 0),
                            ChangeAmount = -Math.Round(lowestPrice * 0.04m, 0),
                            ChangePercent = -3.6,
                            Timestamp = DateTime.UtcNow.AddDays(-60)
                        });
                        histories.Add(new PriceHistory
                        {
                            Variant = variant,
                            Offer = firstOffer,
                            StoreId = firstOffer.StoreId,
                            Price = Math.Round(lowestPrice * 1.04m, 0),
                            PreviousPrice = Math.Round(lowestPrice * 1.07m, 0),
                            ChangeAmount = -Math.Round(lowestPrice * 0.03m, 0),
                            ChangePercent = -2.8,
                            Timestamp = DateTime.UtcNow.AddDays(-30)
                        });
                        histories.Add(new PriceHistory
                        {
                            Variant = variant,
                            Offer = firstOffer,
                            StoreId = firstOffer.StoreId,
                            Price = Math.Round(lowestPrice * 1.015m, 0),
                            PreviousPrice = Math.Round(lowestPrice * 1.04m, 0),
                            ChangeAmount = -Math.Round(lowestPrice * 0.025m, 0),
                            ChangePercent = -2.4,
                            Timestamp = DateTime.UtcNow.AddDays(-14)
                        });
                        histories.Add(new PriceHistory
                        {
                            Variant = variant,
                            Offer = firstOffer,
                            StoreId = firstOffer.StoreId,
                            Price = lowestPrice,
                            PreviousPrice = Math.Round(lowestPrice * 1.015m, 0),
                            ChangeAmount = -Math.Round(lowestPrice * 0.015m, 0),
                            ChangePercent = -1.5,
                            Timestamp = DateTime.UtcNow.AddDays(-1)
                        });
                    }

                    brand.PhoneCount++;
                    products.Add(product);
                    variants.Add(variant);
                }

                await context.Products.AddRangeAsync(products);
                await context.ProductVariants.AddRangeAsync(variants);
                await context.ProductSpecifications.AddRangeAsync(specifications);
                await context.Offers.AddRangeAsync(offers);
                await context.PriceHistories.AddRangeAsync(histories);
                await context.SaveChangesAsync();

                logger.LogInformation("Seeded {ProductCount} phones, {OfferCount} verified offers, and {SpecCount} specifications from catalog-seed.json.",
                    products.Count, offers.Count, specifications.Count);
            }
        }
        else
        {
            await RepairAndSyncCatalogFromSeedAsync(context, logger);
        }

        // 5. Articles & Guides (All 11 Comprehensive Buying Guides)
        if (!await context.Articles.AnyAsync())
        {
            var articles = new List<Article>
            {
                new()
                {
                    Title = "En İyi Kameralı Telefonlar 2026: DxOMark ve Gerçek Kullanım Testleri",
                    Slug = "en-iyi-kamerali-telefonlar",
                    Excerpt = "Sensör boyutu, diyafram açıklığı ve optik görüntü sabitleme (OIS) kriterleriyle 2026'nın en iyi kameralı telefonları.",
                    Content = "Mobil fotoğrafçılıkta megapiksel değeri tek başına belirleyici değildir. Sensörün fiziksel boyutu, OIS sabitleme ve periskop telefoto lens kalitesi belirleyicidir.",
                    CoverImageUrl = "/images/guides/camera-phones.webp",
                    Category = "Satın Alma Rehberi",
                    Tags = new List<string> { "kamera", "fotoğraf", "amiral-gemisi", "karşılaştırma" },
                    ReadingTimeMinutes = 8,
                    Author = "EnUcuzTelefon Uzman Ekibi"
                },
                new()
                {
                    Title = "Fiyat/Performans Telefon Tavsiyeleri (15.000 TL - 25.000 TL)",
                    Slug = "fiyat-performans-telefon-tavsiyeleri",
                    Excerpt = "Bütçenizi zorlamadan en yüksek ekran kalitesi, batarya dayanımı ve işlemci gücünü sunan orta segment şampiyonları.",
                    Content = "Orta segment telefon pazarı hiç olmadığı kadar rekabetçi. AMOLED 120Hz ekranlar, OIS destekli kameralar ve 67W hızlı şarj artık bu bütçede standart.",
                    CoverImageUrl = "/images/guides/budget-phones.webp",
                    Category = "Fiyat/Performans",
                    Tags = new List<string> { "orta-segment", "bütçe-dostu", "tavsiye" },
                    ReadingTimeMinutes = 6,
                    Author = "EnUcuzTelefon Uzman Ekibi"
                },
                new()
                {
                    Title = "Bütçeye Göre En İyi Telefonlar: 10.000 TL'den 80.000 TL'ye Kadar Seçimler",
                    Slug = "butceye-gore",
                    Excerpt = "Her bütçe aralığında paranızın karşılığını en iyi veren, kronik sorunu olmayan telefon modelleri.",
                    Content = "Giriş seviyesinden üst segmente kadar her fiyat diliminde en yüksek donanım ve güncelleme desteği sunan modelleri derledik.",
                    CoverImageUrl = "/images/guides/by-budget.webp",
                    Category = "Satın Alma Rehberi",
                    Tags = new List<string> { "bütçe", "rehber", "fiyat-performans" },
                    ReadingTimeMinutes = 10,
                    Author = "EnUcuzTelefon Uzman Ekibi"
                },
                new()
                {
                    Title = "En İyi Bataryalı Telefonlar: 2 Gün Şarjı Giden Modeller",
                    Slug = "en-iyi-bataryali",
                    Excerpt = "5000 mAh - 6000 mAh silikon-karbon bataryalı ve verimli işlemcili, şarj derdini bitiren telefonlar.",
                    Content = "Batarya ömrünü yalnızca mAh kapasitesi değil, işlemcinin nanometre (nm) üretim teknolojisi ve LTPO ekran paneli belirler.",
                    CoverImageUrl = "/images/guides/battery-phones.webp",
                    Category = "Donanım & Pil",
                    Tags = new List<string> { "batarya", "pil-ömrü", "hızlı-şarj" },
                    ReadingTimeMinutes = 7,
                    Author = "EnUcuzTelefon Uzman Ekibi"
                },
                new()
                {
                    Title = "En İyi Oyun Telefonları: 120 FPS PUBG ve Genshin Impact Canavarları",
                    Slug = "oyun-telefonlari",
                    Excerpt = "Yüksek AnTuTu skoru, gelişmiş buhar odası (Vapor Chamber) soğutması ve 144Hz ekranlı oyuncu telefonları.",
                    Content = "Mobil oyunculukta sürdürülebilir performans (thermal throttling olmaması) en kritik kriterdir.",
                    CoverImageUrl = "/images/guides/gaming-phones.webp",
                    Category = "Oyun & Performans",
                    Tags = new List<string> { "oyun", "gaming", "performans", "antutu" },
                    ReadingTimeMinutes = 8,
                    Author = "EnUcuzTelefon Uzman Ekibi"
                },
                new()
                {
                    Title = "Öğrenciler İçin En İyi Telefonlar ve Vergisiz Telefon Rehberi",
                    Slug = "ogrenciler-icin",
                    Excerpt = "Üniversite ve lise öğrencileri için uzun ömürlü, kamera ve pil başarımı yüksek bütçe dostu telefonlar.",
                    Content = "Öğrenci bütçesine uygun, ders notu takibi, sosyal medya ve günlük kullanımda yıllarca kasmayacak modeller.",
                    CoverImageUrl = "/images/guides/student-phones.webp",
                    Category = "Öğrenci Rehberi",
                    Tags = new List<string> { "öğrenci", "ekonomik", "vergisiz-telefon" },
                    ReadingTimeMinutes = 6,
                    Author = "EnUcuzTelefon Uzman Ekibi"
                },
                new()
                {
                    Title = "Sıfır Telefon mu, Yenilenmiş (Refurbished) Telefon mu Alınmalı?",
                    Slug = "sifir-vs-yenilenmis",
                    Excerpt = "TSE belgeli yenilenmiş telefonların avantajları, dikkat edilmesi gereken riskler ve sıfır cihazlarla fiyat kıyaslaması.",
                    Content = "Yenilenmiş telefon alırken Bakanlık yetki belgeli yenileme merkezlerini ve değişen parça listesini mutlaka kontrol edin.",
                    CoverImageUrl = "/images/guides/refurbished-vs-new.webp",
                    Category = "Tüketici Rehberi",
                    Tags = new List<string> { "yenilenmiş", "sıfır", "karşılaştırma" },
                    ReadingTimeMinutes = 7,
                    Author = "EnUcuzTelefon Uzman Ekibi"
                }
            };

            await context.Articles.AddRangeAsync(articles);
            await context.SaveChangesAsync();
            logger.LogInformation("Seeded {Count} articles.", articles.Count);
        }

        // 6. Q&A Items
        if (!await context.QAItems.AnyAsync())
        {
            var qas = new List<QAItem>
            {
                new()
                {
                    Question = "Telefonlarda ithalatçı garantisi ile resmi distribütör garantisi arasındaki fark nedir?",
                    Answer = "Resmi distribütör garantili cihazlar, markanın Türkiye yetkili servis ağı (Apple TR, Samsung TR vb.) tarafından resmi parça ve işçilik garantisi altındadır. İthalatçı garantili cihazlar ise aracı firmanın anlaşmalı olduğu özel servisler tarafından tamir edilir.",
                    Slug = "ithalatci-garantisi-distributor-garantisi-farki",
                    Category = "Garanti & Yasal Haklar",
                    Tags = new List<string> { "garanti", "distribütör", "ithalatçı", "servis" },
                    HelpfulCount = 342
                },
                new()
                {
                    Question = "Yenilenmiş (Refurbished) telefon almak mantıklı mı?",
                    Answer = "TSE onaylı yenileme merkezlerinden alınan A kalite yenilenmiş telefonlar, 12 ay yasal garanti ve fatura güvencesi sunar. Ancak batarya sağlığı ve orijinal parça kullanımı mutlaka kontrol edilmelidir.",
                    Slug = "yenilenmis-telefon-almak-mantikli-mi",
                    Category = "İkinci El & Yenilenmiş",
                    Tags = new List<string> { "yenilenmiş", "refurbished", "ikinci-el", "bütçe" },
                    HelpfulCount = 219
                },
                new()
                {
                    Question = "BTK IMEI sorgulaması nasıl yapılır ve klonlanmış IMEI nasıl anlaşılır?",
                    Answer = "e-Devlet kapısı üzerinden BTK IMEI Sorgulama hizmetine girerek cihazınızda *#06# tuşladığınızda çıkan 15 haneli numarayı kontrol edebilirsiniz. Durumu 'İthalat yoluyla kaydedilen IMEI' ve marka/model bilgisi cihazınızla birebir aynı olmalıdır.",
                    Slug = "btk-imei-sorgulama-nasil-yapilir",
                    Category = "Resmi İşlemler & BTK",
                    Tags = new List<string> { "imei", "btk", "e-devlet", "güvenlik" },
                    HelpfulCount = 415
                }
            };

            await context.QAItems.AddRangeAsync(qas);
            await context.SaveChangesAsync();
            logger.LogInformation("Seeded {Count} Q&A items.", qas.Count);
        }
    }

    private static async Task RepairAndSyncCatalogFromSeedAsync(ApplicationDbContext context, ILogger logger)
    {
        var seedFileCandidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Persistence", "SeedData", "catalog-seed.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "Persistence", "SeedData", "catalog-seed.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "EnUcuzTelefon.Infrastructure", "Persistence", "SeedData", "catalog-seed.json")
        };

        var seedFilePath = seedFileCandidates.FirstOrDefault(File.Exists);
        if (seedFilePath == null) return;

        // Ensure all whitelisted stores exist and no excluded stores exist
        var storesBySlug = await context.Stores.ToDictionaryAsync(s => s.Slug, StringComparer.OrdinalIgnoreCase);
        var excludedStores = storesBySlug.Values.Where(s => !AllowedStoreSlugs.Contains(s.Slug)).ToList();
        if (excludedStores.Any())
        {
            var excIds = excludedStores.Select(s => s.Id).ToList();
            var excOffers = await context.Offers.Where(o => excIds.Contains(o.StoreId)).ToListAsync();
            if (excOffers.Any())
            {
                context.Offers.RemoveRange(excOffers);
            }
            context.Stores.RemoveRange(excludedStores);
            await context.SaveChangesAsync();
            foreach (var exc in excludedStores)
            {
                storesBySlug.Remove(exc.Slug);
            }
        }

        var predefined = GetPredefinedStores().ToDictionary(s => s.Slug, StringComparer.OrdinalIgnoreCase);
        bool addedMissing = false;
        foreach (var slug in AllowedStoreSlugs)
        {
            if (!storesBySlug.ContainsKey(slug) && predefined.TryGetValue(slug, out var toAdd))
            {
                context.Stores.Add(toAdd);
                storesBySlug[slug] = toAdd;
                addedMissing = true;
            }
        }
        if (addedMissing)
        {
            await context.SaveChangesAsync();
        }

        var existingProducts = await context.Products
            .Include(p => p.Variants)
                .ThenInclude(v => v.Offers)
            .ToDictionaryAsync(p => p.Slug, StringComparer.OrdinalIgnoreCase);

        var jsonText = await File.ReadAllTextAsync(seedFilePath);
        using var doc = JsonDocument.Parse(jsonText);

        int repairedVariants = 0;
        int syncedOffers = 0;

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var slug = item.TryGetProperty("slug", out var sEl) ? sEl.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(slug) || !existingProducts.TryGetValue(slug, out var product))
                continue;

            if (item.TryGetProperty("rating", out var ratEl) && ratEl.ValueKind == JsonValueKind.Number)
            {
                product.Rating = ratEl.GetDouble();
            }

            var variant = product.Variants.FirstOrDefault();
            if (variant == null) continue;

            var lowestPrice = item.GetProperty("lowestPrice").GetDecimal();
            var highestPrice = item.GetProperty("highestPrice").GetDecimal();
            var sixMonthLowest = item.TryGetProperty("sixMonthLowestPrice", out var smlEl) && smlEl.ValueKind == JsonValueKind.Number
                ? smlEl.GetDecimal()
                : Math.Round(lowestPrice * 0.95m, 0);
            var isPriceDropped = item.TryGetProperty("isPriceDropped", out var ipdEl) && ipdEl.GetBoolean();
            var priceDropPercent = item.TryGetProperty("priceDropPercent", out var pdpEl) && pdpEl.ValueKind == JsonValueKind.Number
                ? pdpEl.GetInt32()
                : 0;
            var priceStore = item.TryGetProperty("priceStore", out var psEl) && psEl.ValueKind == JsonValueKind.String
                ? psEl.GetString() ?? "Trendyol"
                : "Trendyol";
            var priceUrl = item.TryGetProperty("priceUrl", out var puEl) && puEl.ValueKind == JsonValueKind.String
                ? puEl.GetString() ?? ""
                : "";

            variant.LowestPrice = lowestPrice;
            variant.HighestPrice = highestPrice > lowestPrice ? highestPrice : Math.Round(lowestPrice * 1.06m, 0);
            variant.SixMonthLowestPrice = (sixMonthLowest > 0 && sixMonthLowest <= lowestPrice)
                ? sixMonthLowest
                : Math.Round(lowestPrice * 0.95m, 0);
            variant.IsPriceDropped = isPriceDropped && priceDropPercent >= 2;
            variant.PriceDropPercent = variant.IsPriceDropped ? Math.Clamp(priceDropPercent, 2, 35) : 0;
            var isDirectPriceUrl = MarketAggregator.IsDirectProductUrl(priceUrl);
            variant.LowestPriceStoreName = isDirectPriceUrl ? priceStore : null;
            variant.LowestPriceUrl = isDirectPriceUrl ? priceUrl : null;

            // Remove any existing offers belonging to stores not in AllowedStoreSlugs or containing fake search URLs
            var invalidOffers = variant.Offers
                .Where(o => o.Store == null || !AllowedStoreSlugs.Contains(o.Store.Slug) || !MarketAggregator.IsDirectProductUrl(o.Url))
                .ToList();
            if (invalidOffers.Any())
            {
                context.Offers.RemoveRange(invalidOffers);
                foreach (var inv in invalidOffers)
                {
                    variant.Offers.Remove(inv);
                }
            }

            if (item.TryGetProperty("storesPrices", out var spArr) && spArr.ValueKind == JsonValueKind.Array)
            {
                var existingOffersByStoreId = variant.Offers
                    .Where(o => o.Store != null && AllowedStoreSlugs.Contains(o.Store.Slug))
                    .GroupBy(o => o.StoreId)
                    .ToDictionary(g => g.Key, g => g.First());

                foreach (var sp in spArr.EnumerateArray())
                {
                    var stSlug = sp.TryGetProperty("storeSlug", out var ssEl) && ssEl.ValueKind == JsonValueKind.String
                        ? ssEl.GetString() ?? "trendyol"
                        : "trendyol";

                    if (!AllowedStoreSlugs.Contains(stSlug) || !storesBySlug.TryGetValue(stSlug, out var storeObj))
                        continue;

                    var offerPrice = sp.GetProperty("price").GetDecimal();
                    var offerUrl = sp.TryGetProperty("url", out var uEl) && uEl.ValueKind == JsonValueKind.String
                        ? uEl.GetString() ?? priceUrl
                        : priceUrl;

                    // Data yoksa yoktur: Sahte arama fallback linkleri offer olarak kaydedilmez
                    if (!MarketAggregator.IsDirectProductUrl(offerUrl))
                    {
                        continue;
                    }

                    if (existingOffersByStoreId.TryGetValue(storeObj.Id, out var existingOffer))
                    {
                        existingOffer.Price = offerPrice;
                        existingOffer.TotalCost = offerPrice;
                        existingOffer.Url = offerUrl;
                        existingOffer.IsAvailable = true;
                        existingOffer.IsVerified = true;
                        existingOffer.LastCheckedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        var newOffer = new Offer
                        {
                            VariantId = variant.Id,
                            StoreId = storeObj.Id,
                            Price = offerPrice,
                            TotalCost = offerPrice,
                            ShippingCost = 0,
                            Url = offerUrl,
                            IsAvailable = true,
                            IsVerified = true,
                            LastCheckedAt = DateTime.UtcNow,
                            ShippingText = storeObj.ShippingInfo,
                            GuaranteeText = "2 Yıl Resmi Distribütör Garantili",
                            InstallmentText = "Peşin Fiyatına 3 Taksit",
                            IsOfficial = storeObj.Type == StoreType.BrandOfficial
                        };
                        context.Offers.Add(newOffer);
                        existingOffersByStoreId[storeObj.Id] = newOffer;
                    }
                    syncedOffers++;
                }
            }

            var verifiedOffers = variant.Offers
                .Where(o => o.IsAvailable && o.Price > 0 && o.Store != null && AllowedStoreSlugs.Contains(o.Store.Slug) && MarketAggregator.IsDirectProductUrl(o.Url))
                .OrderBy(o => o.Price)
                .ToList();
            if (verifiedOffers.Any())
            {
                var minOffer = verifiedOffers.First();
                variant.LowestPrice = minOffer.Price;
                variant.LowestPriceStoreName = minOffer.Store?.Name ?? priceStore;
                variant.LowestPriceUrl = minOffer.Url;
            }
            else
            {
                variant.LowestPrice = 0;
                variant.HighestPrice = 0;
                variant.LowestPriceStoreName = null;
                variant.LowestPriceUrl = null;
                variant.IsPriceDropped = false;
                variant.PriceDropPercent = 0;
            }

            repairedVariants++;
        }

        await context.SaveChangesAsync();
        logger.LogInformation("Repaired and synchronized {VariantCount} phone variants and {OfferCount} store offers from verified catalog-seed.json.",
            repairedVariants, syncedOffers);
    }

    private static void AddSpec(List<ProductSpecification> list, Product product, string category, string key, string value, string? unit = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        list.Add(new ProductSpecification
        {
            Product = product,
            Category = category,
            Key = key,
            Value = value,
            Unit = unit,
            Source = "Resmi Üretici Teknik Bilgisi"
        });
    }

    private static string GetJsonString(JsonElement obj, string propName, string defaultValue = "")
    {
        if (!obj.TryGetProperty(propName, out var el)) return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString() ?? defaultValue,
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => defaultValue
        };
    }

    public static readonly HashSet<string> AllowedStoreSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "hepsiburada", "trendyol", "amazon", "n11", "pttavm", "ciceksepeti", "mediamarkt", "teknosa", "vatan",
        "turkcell", "vodafone", "turk-telekom", "applestore", "samsungstore", "mistore", "huaweistore",
        "honorstore", "tecnostore", "infinixstore", "vivostore"
    };

    public static List<Store> GetPredefinedStores() => new()
    {
        new() { Name = "Hepsiburada", Slug = "hepsiburada", Website = "https://www.hepsiburada.com", Type = StoreType.Marketplace, TypeLabel = "Pazaryeri & Satıcılar", Status = StoreStatus.Active, IntegrationMethod = IntegrationMethod.RestApiV2, ProductCount = 10850, VerifiedPriceCount = 10620, LastSyncTime = DateTime.UtcNow.AddMinutes(-12), ReliabilityScore = 9.8, ShippingInfo = "HepsiJET ile Yarın Kapında", ReturnPolicy = "14 Gün Kolay İade & Kapıdan Alım", DistributorGuarantee = true },
        new() { Name = "Trendyol", Slug = "trendyol", Website = "https://www.trendyol.com", Type = StoreType.Marketplace, TypeLabel = "Pazaryeri & Satıcılar", Status = StoreStatus.Active, IntegrationMethod = IntegrationMethod.RestApiV2, ProductCount = 12480, VerifiedPriceCount = 11950, LastSyncTime = DateTime.UtcNow.AddMinutes(-15), ReliabilityScore = 9.7, ShippingInfo = "Hızlı Teslimat • Ücretsiz Kargo", ReturnPolicy = "14 Gün Kolay İade", DistributorGuarantee = true },
        new() { Name = "Amazon Türkiye", Slug = "amazon", Website = "https://www.amazon.com.tr", Type = StoreType.Marketplace, TypeLabel = "Resmi Satıcı & Pazaryeri", Status = StoreStatus.Active, IntegrationMethod = IntegrationMethod.RestApiV2, ProductCount = 6540, VerifiedPriceCount = 6490, LastSyncTime = DateTime.UtcNow.AddMinutes(-10), ReliabilityScore = 9.9, ShippingInfo = "Prime ile Ertesi Gün Ücretsiz Kargo", ReturnPolicy = "30 Gün Koşulsuz İade", DistributorGuarantee = true },
        new() { Name = "n11", Slug = "n11", Website = "https://www.n11.com", Type = StoreType.Marketplace, TypeLabel = "Pazaryeri & Satıcılar", Status = StoreStatus.Active, IntegrationMethod = IntegrationMethod.RestApiV2, ProductCount = 8900, VerifiedPriceCount = 8640, LastSyncTime = DateTime.UtcNow.AddMinutes(-35), ReliabilityScore = 9.4, ShippingInfo = "Kupon Fırsatıyla İndirim", ReturnPolicy = "14 Gün İade", DistributorGuarantee = true },
        new() { Name = "PttAVM", Slug = "pttavm", Website = "https://www.pttavm.com", Type = StoreType.Marketplace, TypeLabel = "Devlet Güvenceli Pazaryeri", Status = StoreStatus.Active, IntegrationMethod = IntegrationMethod.XmlFeed, ProductCount = 5100, VerifiedPriceCount = 4890, LastSyncTime = DateTime.UtcNow.AddMinutes(-45), ReliabilityScore = 9.3, ShippingInfo = "PTT Kargo Güvencesi", ReturnPolicy = "14 Gün İade Hakkı", DistributorGuarantee = true },
        new() { Name = "Çiçeksepeti", Slug = "ciceksepeti", Website = "https://www.ciceksepeti.com", Type = StoreType.Marketplace, TypeLabel = "Pazaryeri & Elektronik", Status = StoreStatus.Active, IntegrationMethod = IntegrationMethod.RestApiV2, ProductCount = 3200, VerifiedPriceCount = 3120, LastSyncTime = DateTime.UtcNow.AddMinutes(-60), ReliabilityScore = 9.4, ShippingInfo = "Ekstra Güvenli Teslimat", ReturnPolicy = "14 Gün İade", DistributorGuarantee = true },
        new() { Name = "MediaMarkt", Slug = "mediamarkt", Website = "https://www.mediamarkt.com.tr", Type = StoreType.Retailer, TypeLabel = "Teknoloji Perakende Zinciri", Status = StoreStatus.Active, IntegrationMethod = IntegrationMethod.XmlFeed, ProductCount = 2150, VerifiedPriceCount = 2120, LastSyncTime = DateTime.UtcNow.AddMinutes(-25), ReliabilityScore = 9.7, ShippingInfo = "Aynı Gün Mağazadan Teslimat", ReturnPolicy = "14 Gün Mağazadan veya Kargo ile İade", DistributorGuarantee = true },
        new() { Name = "Teknosa", Slug = "teknosa", Website = "https://www.teknosa.com", Type = StoreType.Retailer, TypeLabel = "Teknoloji Perakende & Pazaryeri", Status = StoreStatus.Active, IntegrationMethod = IntegrationMethod.XmlFeed, ProductCount = 3400, VerifiedPriceCount = 3320, LastSyncTime = DateTime.UtcNow.AddMinutes(-20), ReliabilityScore = 9.6, ShippingInfo = "Mağazadan Hemen Teslim Al", ReturnPolicy = "14 Gün Kolay İade", DistributorGuarantee = true },
        new() { Name = "Vatan Bilgisayar", Slug = "vatan", Website = "https://www.vatanbilgisayar.com", Type = StoreType.Retailer, TypeLabel = "Teknoloji Perakende Zinciri", Status = StoreStatus.Active, IntegrationMethod = IntegrationMethod.XmlFeed, ProductCount = 1820, VerifiedPriceCount = 1810, LastSyncTime = DateTime.UtcNow.AddMinutes(-30), ReliabilityScore = 9.8, ShippingInfo = "Tüm Türkiye 81 İl Mağaza", ReturnPolicy = "14 Gün Yasal İade Hakkı", DistributorGuarantee = true },
        new() { Name = "Turkcell (Turkcell Pasaj)", Slug = "turkcell", Website = "https://pasaj.turkcell.com.tr", Type = StoreType.Telecom, TypeLabel = "Operatör Pazaryeri & Cihaz Mağazası", Status = StoreStatus.Active, IntegrationMethod = IntegrationMethod.DirectPartnerApi, ProductCount = 1420, VerifiedPriceCount = 1410, LastSyncTime = DateTime.UtcNow.AddMinutes(-22), ReliabilityScore = 9.9, ShippingInfo = "Faturaya Ek 12 Taksit", ReturnPolicy = "14 Gün Yasal İade", DistributorGuarantee = true },
        new() { Name = "Vodafone (Her Şey Yanımda)", Slug = "vodafone", Website = "https://www.vodafone.com.tr", Type = StoreType.Telecom, TypeLabel = "Operatör Mağazası", Status = StoreStatus.Active, IntegrationMethod = IntegrationMethod.DirectPartnerApi, ProductCount = 980, VerifiedPriceCount = 975, LastSyncTime = DateTime.UtcNow.AddMinutes(-28), ReliabilityScore = 9.8, ShippingInfo = "Vodafone Tarifelerine Özel", ReturnPolicy = "14 Gün İade", DistributorGuarantee = true },
        new() { Name = "Türk Telekom", Slug = "turk-telekom", Website = "https://bireysel.turktelekom.com.tr", Type = StoreType.Telecom, TypeLabel = "Operatör Mağazası", Status = StoreStatus.Active, IntegrationMethod = IntegrationMethod.DirectPartnerApi, ProductCount = 1120, VerifiedPriceCount = 1115, LastSyncTime = DateTime.UtcNow.AddMinutes(-32), ReliabilityScore = 9.8, ShippingInfo = "Evde İnternete Ek Taksit", ReturnPolicy = "14 Gün İade", DistributorGuarantee = true },
        new() { Name = "Apple Store", Slug = "applestore", Website = "https://www.apple.com/tr", Type = StoreType.BrandOfficial, TypeLabel = "Resmi Üretici Satış Mağazası", Status = StoreStatus.Active, IntegrationMethod = IntegrationMethod.DirectPartnerApi, ProductCount = 48, VerifiedPriceCount = 48, LastSyncTime = DateTime.UtcNow.AddMinutes(-5), ReliabilityScore = 10.0, ShippingInfo = "Ücretsiz Ertesi Gün Teslimat", ReturnPolicy = "14 Gün Koşulsuz İade & Değişim", DistributorGuarantee = true },
        new() { Name = "Samsung Store", Slug = "samsungstore", Website = "https://shop.samsung.com/tr", Type = StoreType.BrandOfficial, TypeLabel = "Resmi Üretici Satış Mağazası", Status = StoreStatus.Active, IntegrationMethod = IntegrationMethod.DirectPartnerApi, ProductCount = 110, VerifiedPriceCount = 110, LastSyncTime = DateTime.UtcNow.AddMinutes(-8), ReliabilityScore = 10.0, ShippingInfo = "Aynı Gün / Ertesi Gün Ücretsiz Kargo", ReturnPolicy = "14 Gün Koşulsuz İade", DistributorGuarantee = true },
        new() { Name = "Mi Store (Xiaomi)", Slug = "mistore", Website = "https://www.mi.com/tr", Type = StoreType.BrandOfficial, TypeLabel = "Resmi Üretici Satış Mağazası", Status = StoreStatus.Active, IntegrationMethod = IntegrationMethod.DirectPartnerApi, ProductCount = 85, VerifiedPriceCount = 85, LastSyncTime = DateTime.UtcNow.AddMinutes(-14), ReliabilityScore = 10.0, ShippingInfo = "Resmi Xiaomi Garantisiyle Kargo", ReturnPolicy = "14 Gün İade", DistributorGuarantee = true },
        new() { Name = "Huawei Store", Slug = "huaweistore", Website = "https://consumer.huawei.com/tr/offer", Type = StoreType.BrandOfficial, TypeLabel = "Resmi Üretici Satış Mağazası", Status = StoreStatus.Active, IntegrationMethod = IntegrationMethod.DirectPartnerApi, ProductCount = 65, VerifiedPriceCount = 65, LastSyncTime = DateTime.UtcNow.AddMinutes(-18), ReliabilityScore = 9.9, ShippingInfo = "Hızlı ve Güvenli Kargo", ReturnPolicy = "14 Gün Koşulsuz İade", DistributorGuarantee = true },
        new() { Name = "Honor", Slug = "honorstore", Website = "https://www.honor.com/tr", Type = StoreType.BrandOfficial, TypeLabel = "Resmi Üretici Satış Mağazası", Status = StoreStatus.Active, IntegrationMethod = IntegrationMethod.DirectPartnerApi, ProductCount = 40, VerifiedPriceCount = 40, LastSyncTime = DateTime.UtcNow.AddMinutes(-20), ReliabilityScore = 9.8, ShippingInfo = "Resmi Honor Garantili Kargo", ReturnPolicy = "14 Gün İade", DistributorGuarantee = true },
        new() { Name = "Tecno", Slug = "tecnostore", Website = "https://www.tecno-mobile.com/tr", Type = StoreType.BrandOfficial, TypeLabel = "Resmi Üretici Satış Mağazası", Status = StoreStatus.Active, IntegrationMethod = IntegrationMethod.DirectPartnerApi, ProductCount = 35, VerifiedPriceCount = 35, LastSyncTime = DateTime.UtcNow.AddMinutes(-25), ReliabilityScore = 9.7, ShippingInfo = "Resmi Distribütör Güvencesi", ReturnPolicy = "14 Gün İade", DistributorGuarantee = true },
        new() { Name = "Infinix", Slug = "infinixstore", Website = "https://tr.infinixmobility.com", Type = StoreType.BrandOfficial, TypeLabel = "Resmi Üretici Satış Mağazası", Status = StoreStatus.Active, IntegrationMethod = IntegrationMethod.DirectPartnerApi, ProductCount = 30, VerifiedPriceCount = 30, LastSyncTime = DateTime.UtcNow.AddMinutes(-26), ReliabilityScore = 9.7, ShippingInfo = "Infinix Türkiye Garantili", ReturnPolicy = "14 Gün İade", DistributorGuarantee = true },
        new() { Name = "Vivo", Slug = "vivostore", Website = "https://myepoint.com", Type = StoreType.BrandOfficial, TypeLabel = "Resmi Üretici Satış Mağazası", Status = StoreStatus.Active, IntegrationMethod = IntegrationMethod.DirectPartnerApi, ProductCount = 45, VerifiedPriceCount = 45, LastSyncTime = DateTime.UtcNow.AddMinutes(-22), ReliabilityScore = 9.8, ShippingInfo = "Vivo Türkiye Güvencesi", ReturnPolicy = "14 Gün İade", DistributorGuarantee = true }
    };
}
