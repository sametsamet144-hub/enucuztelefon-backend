namespace EnUcuzTelefon.Application.DTOs;

public record RegisterRequestDto(
    string Email,
    string Password,
    string FullName);

public record LoginRequestDto(
    string Email,
    string Password);

public record AuthResponseDto(
    bool Success,
    string? Token,
    string? Email,
    string? FullName,
    string? Role,
    string? Message);

public record CreatePriceAlertRequestDto(
    string PhoneSlug,
    decimal TargetPrice,
    string Email);

public record PriceAlertDto(
    Guid Id,
    string PhoneSlug,
    string PhoneName,
    decimal TargetPrice,
    decimal CurrentPrice,
    string Email,
    bool IsTriggered,
    DateTime CreatedAt);

public record CompareRequestDto(
    List<string> PhoneSlugs);

public record ProductFilterParams(
    string? Brand = null,
    string? Search = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    int? Ram = null,
    int? Storage = null,
    string? Os = null,
    bool? IsNew = null,
    bool? IsPriceDropped = null,
    string? SortBy = null, // price-asc, price-desc, newest, rating, price-drop
    int Page = 1,
    int PageSize = 20);
