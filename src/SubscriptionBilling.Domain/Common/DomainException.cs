namespace SubscriptionBilling.Domain.Common;

public enum DomainErrorKind
{
    /// <summary>The input breaks an invariant (maps to 400).</summary>
    Validation,

    /// <summary>The operation is not allowed in the current state (maps to 409).</summary>
    Conflict,
}

/// <summary>Raised when a business rule is broken. <see cref="Code"/> is stable and safe to expose to clients.</summary>
public sealed class DomainException(DomainErrorKind kind, string code, string message) : Exception(message)
{
    public DomainErrorKind Kind { get; } = kind;

    public string Code { get; } = code;

    public static DomainException Validation(string code, string message) => new(DomainErrorKind.Validation, code, message);

    public static DomainException Conflict(string code, string message) => new(DomainErrorKind.Conflict, code, message);
}
