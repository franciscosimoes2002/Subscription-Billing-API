using System.ComponentModel.DataAnnotations;

namespace SubscriptionBilling.Api.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required, MinLength(3)]
    public string Issuer { get; init; } = string.Empty;

    [Required, MinLength(3)]
    public string Audience { get; init; } = string.Empty;

    /// <summary>HS256 needs at least 256 bits of key material. Provided through the environment or user-secrets, never committed.</summary>
    [Required, MinLength(32)]
    public string SigningKey { get; init; } = string.Empty;

    [Range(1, 120)]
    public int AccessTokenMinutes { get; init; } = 15;
}

public enum DatabaseInitialization
{
    /// <summary>The schema is managed outside the application (default for production).</summary>
    None,

    /// <summary>Applies pending EF Core migrations at startup.</summary>
    Migrate,

    /// <summary>Creates the schema from the model. Used by the test suite.</summary>
    EnsureCreated,
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public DatabaseInitialization Initialization { get; init; } = DatabaseInitialization.None;
}

/// <summary>Optional first administrator. Both values must come from the environment; nothing is seeded when either is missing.</summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public string? AdminEmail { get; init; }

    public string? AdminPassword { get; init; }
}
