namespace EnUcuzTelefon.Application.DTOs;

public record DisplaySpecsDto(
    double Size,
    string Resolution,
    string Technology,
    int RefreshRate,
    int Brightness,
    string AspectRatio,
    string? Protection,
    string? Hdr,
    int Ppi);

public record MainCameraSpecsDto(
    int Megapixels,
    string Aperture,
    string? SensorSize,
    bool Ois,
    string Autofocus);

public record UltrawideSpecsDto(
    int Megapixels,
    string Aperture,
    int Fov);

public record TelephotoSpecsDto(
    int Megapixels,
    string Aperture,
    int OpticalZoom,
    int? MaxZoom);

public record FrontCameraSpecsDto(
    int Megapixels,
    string Aperture,
    bool Autofocus);

public record CameraSpecsDto(
    MainCameraSpecsDto MainCamera,
    UltrawideSpecsDto? Ultrawide,
    TelephotoSpecsDto? Telephoto,
    FrontCameraSpecsDto FrontCamera,
    List<string> VideoCapabilities,
    List<string>? Features);

public record BatterySpecsDto(
    int Capacity,
    int WiredCharging,
    int? WirelessCharging,
    bool? ReverseWirelessCharging,
    string? ChargingTime,
    bool Removable);

public record PerformanceSpecsDto(
    string Chipset,
    string Cpu,
    string Gpu,
    int Ram,
    int Storage,
    string StorageType,
    bool ExpandableStorage,
    int? AntutuScore = null,
    int? GeekbenchSingle = null,
    int? GeekbenchMulti = null);

public record ConnectivitySpecsDto(
    string Sim,
    bool Nfc,
    string Bluetooth,
    string Wifi,
    string Usb,
    bool FiveG,
    bool Infrared);

public record DesignSpecsDto(
    double Width,
    double Height,
    double Thickness,
    double Weight,
    string Materials,
    List<string> Colors,
    string WaterResistance,
    string Fingerprint,
    bool FaceId);

public record SoftwareSpecsDto(
    string Os,
    string? UpdatePolicy);

public record PhoneSpecsDto(
    DisplaySpecsDto Display,
    CameraSpecsDto Camera,
    BatterySpecsDto Battery,
    PerformanceSpecsDto Performance,
    ConnectivitySpecsDto Connectivity,
    DesignSpecsDto? Design,
    SoftwareSpecsDto? Software);

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
    string ReleaseDate,
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
