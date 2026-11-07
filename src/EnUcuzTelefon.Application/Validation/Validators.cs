using EnUcuzTelefon.Application.DTOs;
using FluentValidation;

namespace EnUcuzTelefon.Application.Validation;

public class RegisterRequestValidator : AbstractValidator<RegisterRequestDto>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(6);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
    }
}

public class LoginRequestValidator : AbstractValidator<LoginRequestDto>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public class CreatePriceAlertRequestValidator : AbstractValidator<CreatePriceAlertRequestDto>
{
    public CreatePriceAlertRequestValidator()
    {
        RuleFor(x => x.PhoneSlug).NotEmpty();
        RuleFor(x => x.TargetPrice).GreaterThan(0);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}
