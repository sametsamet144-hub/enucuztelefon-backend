namespace EnUcuzTelefon.Shared.Results;

public class Error
{
    public string Code { get; }
    public string Message { get; }

    public static readonly Error None = new(string.Empty, string.Empty);
    public static readonly Error NullValue = new("Error.NullValue", "The specified value is null.");

    public Error(string code, string message)
    {
        Code = code;
        Message = message;
    }

    public static Error NotFound(string code, string message) => new(code, message);
    public static Error Validation(string code, string message) => new(code, message);
    public static Error Conflict(string code, string message) => new(code, message);
    public static Error Failure(string code, string message) => new(code, message);
}
