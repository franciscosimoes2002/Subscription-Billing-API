using SubscriptionBilling.Domain.Common;

namespace SubscriptionBilling.Domain.Plans;

public sealed class Plan : Entity
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;
    public const long MaxPriceCents = 100_000_00;

    private Plan()
    {
        // Required by EF Core.
    }

    public string Name { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    /// <summary>Price in the smallest currency unit (cents). Integers avoid floating-point money bugs.</summary>
    public long PriceCents { get; private set; }

    /// <summary>ISO 4217 code, upper case.</summary>
    public string Currency { get; private set; } = string.Empty;

    public BillingInterval Interval { get; private set; }

    public bool IsActive { get; private set; }

    public bool CanBeSubscribed => IsActive && !IsDeleted;

    public static Plan Create(string name, string? description, long priceCents, string currency, BillingInterval interval, DateTime nowUtc)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));
        var plan = new Plan { CreatedAtUtc = nowUtc, IsActive = true };
        plan.Apply(name, description, priceCents, currency, interval);
        return plan;
    }

    public void Update(string name, string? description, long priceCents, string currency, BillingInterval interval)
    {
        if (IsDeleted)
        {
            throw DomainException.Conflict("plan_deleted", "A deleted plan cannot be changed.");
        }

        Apply(name, description, priceCents, currency, interval);
    }

    public void Deactivate() => IsActive = false;

    public void Activate()
    {
        if (IsDeleted)
        {
            throw DomainException.Conflict("plan_deleted", "A deleted plan cannot be activated.");
        }

        IsActive = true;
    }

    private void Apply(string name, string? description, long priceCents, string currency, BillingInterval interval)
    {
        var cleanName = (name ?? string.Empty).Trim();
        if (cleanName.Length is < 2 or > NameMaxLength)
        {
            throw DomainException.Validation("invalid_plan_name", $"The plan name must have between 2 and {NameMaxLength} characters.");
        }

        var cleanDescription = (description ?? string.Empty).Trim();
        if (cleanDescription.Length > DescriptionMaxLength)
        {
            throw DomainException.Validation("invalid_plan_description", $"The description must have at most {DescriptionMaxLength} characters.");
        }

        if (priceCents is < 0 or > MaxPriceCents)
        {
            throw DomainException.Validation("invalid_plan_price", "The price is out of range.");
        }

        var code = (currency ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length != 3 || !code.All(char.IsAsciiLetterUpper))
        {
            throw DomainException.Validation("invalid_currency", "The currency must be a three-letter ISO 4217 code.");
        }

        if (!Enum.IsDefined(interval))
        {
            throw DomainException.Validation("invalid_interval", "The billing interval is not valid.");
        }

        Name = cleanName;
        Description = cleanDescription;
        PriceCents = priceCents;
        Currency = code;
        Interval = interval;
    }
}
