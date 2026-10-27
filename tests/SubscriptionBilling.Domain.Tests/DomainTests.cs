using SubscriptionBilling.Domain.Common;
using SubscriptionBilling.Domain.Plans;
using SubscriptionBilling.Domain.Subscriptions;
using SubscriptionBilling.Domain.Users;

namespace SubscriptionBilling.Domain.Tests;

public class UserTests
{
    private static readonly DateTime Now = new(2026, 3, 15, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Register_normalises_the_email_and_trims_the_name()
    {
        var user = User.Register("  Ana.Silva@Example.COM ", "  Ana Silva ", "hash", UserRole.User, Now);

        Assert.Equal("ana.silva@example.com", user.Email);
        Assert.Equal("Ana Silva", user.FullName);
        Assert.Equal(UserRole.User, user.Role);
        Assert.Equal(Now, user.CreatedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-at-sign")]
    [InlineData("a@b")]
    public void Register_rejects_invalid_emails(string email)
    {
        var error = Assert.Throws<DomainException>(() => User.Register(email, "Ana Silva", "hash", UserRole.User, Now));
        Assert.Equal("invalid_email", error.Code);
        Assert.Equal(DomainErrorKind.Validation, error.Kind);
    }

    [Fact]
    public void Register_rejects_a_blank_password_hash()
    {
        var error = Assert.Throws<DomainException>(() => User.Register("ana@example.com", "Ana Silva", " ", UserRole.User, Now));
        Assert.Equal("invalid_password_hash", error.Code);
    }

    [Fact]
    public void Timestamps_must_be_utc()
    {
        var local = new DateTime(2026, 3, 15, 10, 0, 0, DateTimeKind.Unspecified);
        Assert.Throws<ArgumentException>(() => User.Register("ana@example.com", "Ana Silva", "hash", UserRole.User, local));
    }
}

public class PlanTests
{
    private static readonly DateTime Now = new(2026, 3, 15, 10, 0, 0, DateTimeKind.Utc);

    private static Plan NewPlan() => Plan.Create("Pro", "For teams", 1999, "eur", BillingInterval.Monthly, Now);

    [Fact]
    public void Create_normalises_the_currency_and_starts_active()
    {
        var plan = NewPlan();

        Assert.Equal("EUR", plan.Currency);
        Assert.Equal(1999, plan.PriceCents);
        Assert.True(plan.IsActive);
        Assert.True(plan.CanBeSubscribed);
    }

    [Theory]
    [InlineData("", "invalid_plan_name")]
    [InlineData("x", "invalid_plan_name")]
    public void Create_rejects_bad_names(string name, string code)
    {
        var error = Assert.Throws<DomainException>(() => Plan.Create(name, null, 100, "EUR", BillingInterval.Monthly, Now));
        Assert.Equal(code, error.Code);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(Plan.MaxPriceCents + 1)]
    public void Create_rejects_prices_out_of_range(long price)
    {
        var error = Assert.Throws<DomainException>(() => Plan.Create("Pro", null, price, "EUR", BillingInterval.Monthly, Now));
        Assert.Equal("invalid_plan_price", error.Code);
    }

    [Theory]
    [InlineData("EU")]
    [InlineData("EURO")]
    [InlineData("12E")]
    public void Create_rejects_bad_currency_codes(string currency)
    {
        var error = Assert.Throws<DomainException>(() => Plan.Create("Pro", null, 100, currency, BillingInterval.Monthly, Now));
        Assert.Equal("invalid_currency", error.Code);
    }

    [Fact]
    public void Create_rejects_an_undefined_interval()
    {
        var error = Assert.Throws<DomainException>(() => Plan.Create("Pro", null, 100, "EUR", (BillingInterval)42, Now));
        Assert.Equal("invalid_interval", error.Code);
    }

    [Fact]
    public void A_deleted_plan_cannot_be_updated_or_activated_and_cannot_be_subscribed()
    {
        var plan = NewPlan();
        plan.SoftDelete(Now);

        Assert.False(plan.CanBeSubscribed);
        Assert.Equal("plan_deleted", Assert.Throws<DomainException>(() => plan.Update("Pro 2", null, 1, "EUR", BillingInterval.Yearly)).Code);
        Assert.Equal("plan_deleted", Assert.Throws<DomainException>(plan.Activate).Code);
    }

    [Fact]
    public void Deactivating_a_plan_stops_new_subscriptions()
    {
        var plan = NewPlan();
        plan.Deactivate();
        Assert.False(plan.CanBeSubscribed);
    }
}

public class SubscriptionTests
{
    private static readonly DateTime Now = new(2026, 1, 31, 9, 30, 0, DateTimeKind.Utc);
    private static readonly Guid UserId = Guid.CreateVersion7();

    private static Plan NewPlan(BillingInterval interval = BillingInterval.Monthly) => Plan.Create("Pro", null, 1999, "EUR", interval, Now);

    [Fact]
    public void Start_copies_the_plan_terms_so_later_price_changes_do_not_rewrite_history()
    {
        var plan = NewPlan();
        var subscription = Subscription.Start(UserId, plan, Now);

        plan.Update("Pro", null, 4999, "EUR", BillingInterval.Monthly);

        Assert.Equal(1999, subscription.PriceCents);
        Assert.Equal("Pro", subscription.PlanName);
        Assert.Equal("EUR", subscription.Currency);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Fact]
    public void A_monthly_period_clamps_to_the_end_of_a_shorter_month()
    {
        var subscription = Subscription.Start(UserId, NewPlan(BillingInterval.Monthly), Now);
        Assert.Equal(new DateTime(2026, 2, 28, 9, 30, 0, DateTimeKind.Utc), subscription.CurrentPeriodEndUtc);
    }

    [Fact]
    public void A_yearly_period_ends_one_year_later()
    {
        var subscription = Subscription.Start(UserId, NewPlan(BillingInterval.Yearly), Now);
        Assert.Equal(new DateTime(2027, 1, 31, 9, 30, 0, DateTimeKind.Utc), subscription.CurrentPeriodEndUtc);
    }

    [Fact]
    public void Start_refuses_plans_that_are_not_on_sale()
    {
        var inactive = NewPlan();
        inactive.Deactivate();
        var deleted = NewPlan();
        deleted.SoftDelete(Now);

        Assert.Equal("plan_unavailable", Assert.Throws<DomainException>(() => Subscription.Start(UserId, inactive, Now)).Code);
        Assert.Equal("plan_unavailable", Assert.Throws<DomainException>(() => Subscription.Start(UserId, deleted, Now)).Code);
    }

    [Fact]
    public void Start_needs_a_user()
    {
        var error = Assert.Throws<DomainException>(() => Subscription.Start(Guid.Empty, NewPlan(), Now));
        Assert.Equal("invalid_user", error.Code);
    }

    [Fact]
    public void Cancel_records_the_moment_and_cannot_be_repeated()
    {
        var subscription = Subscription.Start(UserId, NewPlan(), Now);
        var later = Now.AddDays(3);

        subscription.Cancel(later);

        Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);
        Assert.Equal(later, subscription.CancelledAtUtc);
        var error = Assert.Throws<DomainException>(() => subscription.Cancel(later.AddDays(1)));
        Assert.Equal("subscription_already_cancelled", error.Code);
        Assert.Equal(DomainErrorKind.Conflict, error.Kind);
    }
}
