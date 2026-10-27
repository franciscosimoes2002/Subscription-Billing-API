using System.ComponentModel.DataAnnotations;
using SubscriptionBilling.Domain.Users;

namespace SubscriptionBilling.Application.Auth;

public sealed record RegisterRequest(
    [Required, EmailAddress, StringLength(User.EmailMaxLength)] string Email,
    [Required, StringLength(User.FullNameMaxLength, MinimumLength = 2)] string FullName,
    [Required, StringLength(PasswordPolicy.MaxLength, MinimumLength = PasswordPolicy.MinLength)] string Password);

public sealed record LoginRequest(
    [Required, StringLength(User.EmailMaxLength)] string Email,
    [Required, StringLength(PasswordPolicy.MaxLength)] string Password);

public sealed record UserDto(Guid Id, string Email, string FullName, string Role)
{
    public static UserDto From(User user) => new(user.Id, user.Email, user.FullName, user.Role.ToString());
}

public sealed record AuthResponse(string AccessToken, string TokenType, DateTime ExpiresAtUtc, UserDto User);

public static class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 128;

    /// <summary>Length-based policy (NIST SP 800-63B): no composition rules, but long enough and not blank.</summary>
    public static bool IsAcceptable(string? password) =>
        password is { Length: >= MinLength and <= MaxLength } && !string.IsNullOrWhiteSpace(password);
}
