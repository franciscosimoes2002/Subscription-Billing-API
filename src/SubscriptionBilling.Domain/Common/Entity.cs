namespace SubscriptionBilling.Domain.Common;

/// <summary>Base type: time-ordered identifier (UUID v7) and soft delete.</summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();

    public DateTime CreatedAtUtc { get; protected set; }

    public bool IsDeleted { get; private set; }

    public DateTime? DeletedAtUtc { get; private set; }

    public void SoftDelete(DateTime nowUtc)
    {
        if (IsDeleted)
        {
            return;
        }

        EnsureUtc(nowUtc, nameof(nowUtc));
        IsDeleted = true;
        DeletedAtUtc = nowUtc;
    }

    protected static void EnsureUtc(DateTime value, string parameter)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamps must be UTC.", parameter);
        }
    }
}
