namespace EnUcuzTelefon.Application.DTOs;

public record DisplaySpecsDto(
    double? Size = null,
    string? Resolution = null,
    string? Technology = null,
    int? RefreshRate = null,
    int? Brightness = null,
    string? AspectRatio = null,
    string? Protection = null,
    string? Hdr = null,
    int? Ppi = null);

public record MainCameraSpecsDto(
    int? Megapixels = null,
    string? Aperture = null,
    string? SensorSize = null,
    bool? Ois = null,
    string? Autofocus = null);

public record UltrawideSpecsDto(
    int? Megapixels = null,
    string? Aperture = null,
    int? Fov = null);

public record TelephotoSpecsDto(
    int? Megapixels = null,
    string? Aperture = null,
    int? OpticalZoom = null,
    int? MaxZoom = null);

public record FrontCameraSpecsDto(
    int? Megapixels = null,
    string? Aperture = null,
    bool? Autofocus = null);

public record CameraSpecsDto(
    MainCameraSpecsDto? MainCamera = null,
    UltrawideSpecsDto? Ultrawide = null,
    TelephotoSpecsDto? Telephoto = null,
    FrontCameraSpecsDto? FrontCamera = null,
    List<string>? VideoCapabilities = null,
    List<string>? Features = null);

public record BatterySpecsDto(
    int? Capacity = null,
    int? WiredCharging = null,
    int? WirelessCharging = null,
    bool? ReverseWirelessCharging = null,
    string? ChargingTime = null,
    bool? Removable = null);

public record PerformanceSpecsDto(
    string? Chipset = null,
    string? Cpu = null,
    string? Gpu = null,
    int? Ram = null,
    int? Storage = null,
    string? StorageType = null,
    bool? ExpandableStorage = null,
    int? AntutuScore = null,
    int? GeekbenchSingle = null,
    int? GeekbenchMulti = null);

public record ConnectivitySpecsDto(
    string? Sim = null,
    bool? Nfc = null,
    string? Bluetooth = null,
    string? Wifi = null,
    string? Usb = null,
    bool? FiveG = null,
    bool? Infrared = null);

public record DesignSpecsDto(
    double? Width = null,
    double? Height = null,
    double? Thickness = null,
    double? Weight = null,
    string? Materials = null,
    List<string>? Colors = null,
    string? WaterResistance = null,
    string? Fingerprint = null,
    bool? FaceId = null);

public record SoftwareSpecsDto(
    string? Os = null,
    string? UpdatePolicy = null);

public record PhoneSpecsDto(
    DisplaySpecsDto? Display = null,
    CameraSpecsDto? Camera = null,
    BatterySpecsDto? Battery = null,
    PerformanceSpecsDto? Performance = null,
    ConnectivitySpecsDto? Connectivity = null,
    DesignSpecsDto? Design = null,
    SoftwareSpecsDto? Software = null);

public record StorePriceDto(
    string Store,
    string StoreSlug,
    string? SellerName,
    double? SellerRating,
    decimal Price,
    string Url,
    bool IsAvailable,
    string LastUpdated,
    string? ShippingText,
    string? GuaranteeText,
    string? InstallmentText,
    bool? IsOfficial);

public record PriceHistoryDto(
    string Date,
    decimal Price,
    string Store);

public record ProductPricesResponseDto(
    string Id,
    string PhoneSlug,
    decimal LowestPrice,
    decimal HighestPrice,
    int TotalStores,
    List<StorePriceDto> StorePrices,
    string LastUpdated);

public record PhoneDto(
    string Id,
    string Slug,
    string Name,
    string Brand,
    string BrandSlug,
    string Image,
    List<string> Images,
    decimal LowestPrice,
    decimal HighestPrice,
    decimal? SixMonthLowestPrice,
    string? PriceStore,
    string? PriceUrl,
    string? ReleaseDate,
    bool IsNew,
    bool IsPriceDropped,
    int? PriceDropPercent,
    PhoneSpecsDto Specs,
    List<StorePriceDto> StoresPrices,
    double? Rating,
    int? ReviewCount,
    List<string>? Tags,
    string? Description);

public record BrandDto(
    string Id,
    string Slug,
    string Name,
    string Logo,
    int PhoneCount,
    string Country,
    string? Description,
    string? Website,
    int? FoundedYear);

public record StoreDto(
    string Id,
    string Name,
    string Slug,
    string Website,
    string Type,
    string TypeLabel,
    string Status,
    string IntegrationMethod,
    int ProductCount,
    int VerifiedPriceCount,
    string LastSyncTime,
    string? LastErrorTime,
    int FailedRecordCount,
    bool IsVerified,
    double ReliabilityScore,
    string ShippingInfo,
    string ReturnPolicy,
    bool DistributorGuarantee);

public record ArticleDto(
    string Id,
    string Slug,
    string Title,
    string Excerpt,
    string Content,
    string? CoverImage,
    string Category,
    List<string> Tags,
    string PublishedAt,
    string? UpdatedAt,
    int ReadingTime,
    string? Author);

public record QAItemDto(
    string Id,
    string Slug,
    string Question,
    string Answer,
    string Category,
    List<string> Tags,
    int Helpful,
    string? PublishedAt);

public record AdminDashboardStatsDto(
    int TotalPhones,
    int TotalVariants,
    int TotalBrands,
    int TotalStores,
    int ActiveIntegrations,
    int TotalOffers,
    int VerifiedPricesCount,
    int PriceDropCount,
    int SuspiciousPricesCount,
    int PendingMatchesCount,
    DateTime LastUpdated);
