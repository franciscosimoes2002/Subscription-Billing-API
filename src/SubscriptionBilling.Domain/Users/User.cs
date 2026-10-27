using SubscriptionBilling.Domain.Common;

namespace SubscriptionBilling.Domain.Users;

public sealed class User : Entity
{
    public const int EmailMaxLength = 254;
    public const int FullNameMaxLength = 100;

    private User()
    {
        // Required by EF Core.
    }

    public string Email { get; private set; } = string.Empty;

    public string FullName { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public UserRole Role { get; private set; }

    public static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    public static User Register(string email, string fullName, string passwordHash, UserRole role, DateTime nowUtc)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));
        var normalized = NormalizeEmail(email);
        if (normalized.Length is < 5 or > EmailMaxLength || !normalized.Contains('@', StringComparison.Ordinal))
        {
            throw DomainException.Validation("invalid_email", "The email address is not valid.");
        }

        var name = (fullName ?? string.Empty).Trim();
        if (name.Length is < 2 or > FullNameMaxLength)
        {
            throw DomainException.Validation("invalid_name", $"The name must have between 2 and {FullNameMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw DomainException.Validation("invalid_password_hash", "A password hash is required.");
        }

        if (!Enum.IsDefined(role))
        {
            throw DomainException.Validation("invalid_role", "The role is not valid.");
        }

        return new User
        {
            Email = normalized,
            FullName = name,
            PasswordHash = passwordHash,
            Role = role,
            CreatedAtUtc = nowUtc,
        };
    }
}
