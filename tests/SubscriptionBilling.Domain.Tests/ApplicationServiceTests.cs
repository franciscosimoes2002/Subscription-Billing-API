using Microsoft.Extensions.Logging.Abstractions;
using SubscriptionBilling.Application.Abstractions;
using SubscriptionBilling.Application.Auth;
using SubscriptionBilling.Application.Common;
using SubscriptionBilling.Application.Plans;
using SubscriptionBilling.Application.Subscriptions;
using SubscriptionBilling.Domain.Plans;
using SubscriptionBilling.Domain.Subscriptions;
using SubscriptionBilling.Domain.Users;

namespace SubscriptionBilling.Domain.Tests;

/// <summary>In-memory ports: the application services are tested for their decisions, without a database or a web server.</summary>
internal sealed class InMemoryStore : IUserRepository, IPlanRepository, ISubscriptionRepository, IUnitOfWork
{
    public List<User> Users { get; } = [];

    public List<Plan> Plans { get; } = [];

    public List<Subscription> Subscriptions { get; } = [];

    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Users.FirstOrDefault(x => x.Id == id));

    public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) => Task.FromResult(Users.FirstOrDefault(x => x.Email == normalizedEmail));

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken) => Task.FromResult(Users.Any(x => x.Email == normalizedEmail));

    public void Add(User user) => Users.Add(user);

    Task<Plan?> IPlanRepository.FindByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Plans.FirstOrDefault(x => x.Id == id && !x.IsDeleted));

    public Task<PagedResult<Plan>> ListAsync(PageRequest page, bool activeOnly, CancellationToken cancellationToken)
    {
        var items = Plans.Where(x => !x.IsDeleted && (!activeOnly || x.IsActive)).ToList();
        return Task.FromResult(new PagedResult<Plan>([.. items.Skip(page.Skip).Take(page.PageSize)], page.Page, page.PageSize, items.Count));
    }

    public Task<bool> NameExistsAsync(string name, Guid? excludingId, CancellationToken cancellationToken) =>
        Task.FromResult(Plans.Any(x => !x.IsDeleted && x.Name == name && x.Id != excludingId));

    public void Add(Plan plan) => Plans.Add(plan);

    public Task<Subscription?> FindForUserAsync(Guid userId, Guid subscriptionId, CancellationToken cancellationToken) =>
        Task.FromResult(Subscriptions.FirstOrDefault(x => x.Id == subscriptionId && x.UserId == userId));

    public Task<bool> HasActiveAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Subscriptions.Any(x => x.UserId == userId && x.Status == SubscriptionStatus.Active));

    public Task<PagedResult<Subscription>> ListByUserAsync(Guid userId, PageRequest page, CancellationToken cancellationToken)
    {
        var items = Subscriptions.Where(x => x.UserId == userId).ToList();
        return Task.FromResult(new PagedResult<Subscription>([.. items.Skip(page.Skip).Take(page.PageSize)], page.Page, page.PageSize, items.Count));
    }

    public void Add(Subscription subscription) => Subscriptions.Add(subscription);

    /// <summary>Set to simulate a concurrent request winning the race on a unique index.</summary>
    public bool FailNextSaveWithUniqueViolation { get; set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (FailNextSaveWithUniqueViolation)
        {
            FailNextSaveWithUniqueViolation = false;
            throw new UniqueConstraintViolationException(new InvalidOperationException("simulated"));
        }

        return Task.CompletedTask;
    }
}

internal sealed class FakeHasher : IPasswordHasher
{
    public string Hash(string password) => $"hashed:{password}";

    public bool Verify(string? hash, string password) => hash is not null && hash == Hash(password);
}

internal sealed class FakeTokens : ITokenService
{
    public AccessToken Issue(User user, DateTime nowUtc) => new($"token-for-{user.Id}", nowUtc.AddMinutes(15));
}

public class AuthServiceTests
{
    private const string GoodPassword = "correct horse battery";

    private readonly InMemoryStore _store = new();

    private AuthService Create() => new(_store, _store, new FakeHasher(), new FakeTokens(), TimeProvider.System, NullLogger<AuthService>.Instance);

    [Fact]
    public async Task Register_creates_a_user_with_the_user_role_and_stores_the_hash_not_the_password()
    {
        var result = await Create().RegisterAsync(new RegisterRequest("Ana@Example.com", "Ana Silva", GoodPassword), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("ana@example.com", result.Value.Email);
        Assert.Equal("User", result.Value.Role);
        var stored = Assert.Single(_store.Users);
        Assert.Equal(new FakeHasher().Hash(GoodPassword), stored.PasswordHash);
        Assert.NotEqual(GoodPassword, stored.PasswordHash);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("            ")]
    public async Task Register_rejects_weak_passwords(string password)
    {
        var result = await Create().RegisterAsync(new RegisterRequest("ana@example.com", "Ana Silva", password), TestContext.Current.CancellationToken);

        Assert.Equal("weak_password", result.Failure?.Code);
        Assert.Empty(_store.Users);
    }

    [Fact]
    public async Task Register_reports_a_duplicate_email_as_a_conflict_regardless_of_case()
    {
        var service = Create();
        await service.RegisterAsync(new RegisterRequest("ana@example.com", "Ana Silva", GoodPassword), TestContext.Current.CancellationToken);

        var result = await service.RegisterAsync(new RegisterRequest("ANA@example.com", "Other Ana", GoodPassword), TestContext.Current.CancellationToken);

        Assert.Equal(FailureKind.Conflict, result.Failure?.Kind);
        Assert.Equal("email_taken", result.Failure?.Code);
    }

    [Fact]
    public async Task Register_handles_losing_a_race_on_the_unique_index()
    {
        _store.FailNextSaveWithUniqueViolation = true;

        var result = await Create().RegisterAsync(new RegisterRequest("ana@example.com", "Ana Silva", GoodPassword), TestContext.Current.CancellationToken);

        Assert.Equal("email_taken", result.Failure?.Code);
    }

    [Fact]
    public async Task Login_gives_the_same_answer_for_an_unknown_email_and_a_wrong_password()
    {
        var service = Create();
        await service.RegisterAsync(new RegisterRequest("ana@example.com", "Ana Silva", GoodPassword), TestContext.Current.CancellationToken);

        var unknown = await service.LoginAsync(new LoginRequest("nobody@example.com", GoodPassword), TestContext.Current.CancellationToken);
        var wrong = await service.LoginAsync(new LoginRequest("ana@example.com", "not the password"), TestContext.Current.CancellationToken);

        Assert.Equal(unknown.Failure, wrong.Failure);
        Assert.Equal(FailureKind.Unauthorized, wrong.Failure?.Kind);
    }

    [Fact]
    public async Task Login_returns_a_bearer_token_for_valid_credentials()
    {
        var service = Create();
        await service.RegisterAsync(new RegisterRequest("ana@example.com", "Ana Silva", GoodPassword), TestContext.Current.CancellationToken);

        var result = await service.LoginAsync(new LoginRequest(" ANA@example.com ", GoodPassword), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("Bearer", result.Value.TokenType);
        Assert.StartsWith("token-for-", result.Value.AccessToken, StringComparison.Ordinal);
    }
}

public class PlanServiceTests
{
    private readonly InMemoryStore _store = new();

    private PlanService Create() => new(_store, _store, TimeProvider.System, NullLogger<PlanService>.Instance);

    private static PlanRequest Request(string name = "Pro", long price = 1999) => new(name, "desc", price, "EUR", BillingInterval.Monthly);

    [Fact]
    public async Task Create_rejects_a_duplicate_name()
    {
        var service = Create();
        await service.CreateAsync(Request(), TestContext.Current.CancellationToken);

        var result = await service.CreateAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal("plan_name_taken", result.Failure?.Code);
    }

    [Fact]
    public async Task The_public_catalogue_hides_inactive_plans_but_the_back_office_sees_them()
    {
        var service = Create();
        await service.CreateAsync(Request("Free", 0), TestContext.Current.CancellationToken);
        await service.CreateAsync(Request("Legacy", 999), TestContext.Current.CancellationToken);
        _store.Plans.Single(x => x.Name == "Legacy").Deactivate();

        var publicList = await service.ListAsync(new PageRequest(), TestContext.Current.CancellationToken);
        var adminList = await service.ListAllAsync(new PageRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(["Free"], publicList.Items.Select(x => x.Name));
        Assert.Equal(2, adminList.TotalCount);
    }

    [Fact]
    public async Task Delete_is_soft_and_the_plan_disappears_from_lookups()
    {
        var service = Create();
        var created = (await service.CreateAsync(Request(), TestContext.Current.CancellationToken)).Value;

        var deleted = await service.DeleteAsync(created.Id, TestContext.Current.CancellationToken);
        var lookup = await service.GetAsync(created.Id, includeInactive: true, TestContext.Current.CancellationToken);

        Assert.True(deleted.IsSuccess);
        Assert.Equal("plan_not_found", lookup.Failure?.Code);
        Assert.True(_store.Plans.Single().IsDeleted);
    }

    [Fact]
    public async Task Update_cannot_take_the_name_of_another_plan()
    {
        var service = Create();
        await service.CreateAsync(Request("Free", 0), TestContext.Current.CancellationToken);
        var pro = (await service.CreateAsync(Request("Pro"), TestContext.Current.CancellationToken)).Value;

        var result = await service.UpdateAsync(pro.Id, Request("Free"), TestContext.Current.CancellationToken);

        Assert.Equal("plan_name_taken", result.Failure?.Code);
    }
}

public class SubscriptionServiceTests
{
    private readonly InMemoryStore _store = new();
    private readonly Guid _userId = Guid.CreateVersion7();

    private SubscriptionService Create() => new(_store, _store, _store, TimeProvider.System, NullLogger<SubscriptionService>.Instance);

    private Plan AddPlan(string name = "Pro")
    {
        var plan = Plan.Create(name, null, 1999, "EUR", BillingInterval.Monthly, DateTime.UtcNow);
        _store.Plans.Add(plan);
        return plan;
    }

    [Fact]
    public async Task Subscribe_then_subscribe_again_is_a_conflict()
    {
        var plan = AddPlan();
        var service = Create();

        var first = await service.SubscribeAsync(_userId, plan.Id, TestContext.Current.CancellationToken);
        var second = await service.SubscribeAsync(_userId, plan.Id, TestContext.Current.CancellationToken);

        Assert.True(first.IsSuccess);
        Assert.Equal("already_subscribed", second.Failure?.Code);
    }

    [Fact]
    public async Task Losing_the_race_on_the_unique_index_is_reported_as_already_subscribed()
    {
        var plan = AddPlan();
        _store.FailNextSaveWithUniqueViolation = true;

        var result = await Create().SubscribeAsync(_userId, plan.Id, TestContext.Current.CancellationToken);

        Assert.Equal("already_subscribed", result.Failure?.Code);
    }

    [Fact]
    public async Task An_inactive_plan_looks_exactly_like_a_missing_one()
    {
        var plan = AddPlan();
        plan.Deactivate();
        var service = Create();

        var inactive = await service.SubscribeAsync(_userId, plan.Id, TestContext.Current.CancellationToken);
        var missing = await service.SubscribeAsync(_userId, Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        Assert.Equal(missing.Failure, inactive.Failure);
    }

    [Fact]
    public async Task Someone_elses_subscription_is_not_found_rather_than_forbidden()
    {
        var plan = AddPlan();
        var service = Create();
        var mine = (await service.SubscribeAsync(_userId, plan.Id, TestContext.Current.CancellationToken)).Value;

        var cancel = await service.CancelAsync(Guid.CreateVersion7(), mine.Id, TestContext.Current.CancellationToken);
        var read = await service.GetAsync(Guid.CreateVersion7(), mine.Id, TestContext.Current.CancellationToken);

        Assert.Equal(FailureKind.NotFound, cancel.Failure?.Kind);
        Assert.Equal(FailureKind.NotFound, read.Failure?.Kind);
    }

    [Fact]
    public async Task After_cancelling_the_user_can_subscribe_again()
    {
        var plan = AddPlan();
        var service = Create();
        var first = (await service.SubscribeAsync(_userId, plan.Id, TestContext.Current.CancellationToken)).Value;

        var cancelled = await service.CancelAsync(_userId, first.Id, TestContext.Current.CancellationToken);
        var again = await service.SubscribeAsync(_userId, plan.Id, TestContext.Current.CancellationToken);

        Assert.Equal("Cancelled", cancelled.Value.Status);
        Assert.True(again.IsSuccess);
    }
}

public class PageRequestTests
{
    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(-5, 1000, 1, 100)]
    [InlineData(3, 20, 3, 20)]
    public void Page_requests_are_always_bounded(int page, int size, int expectedPage, int expectedSize)
    {
        var request = new PageRequest(page, size);

        Assert.Equal(expectedPage, request.Page);
        Assert.Equal(expectedSize, request.PageSize);
    }

    [Fact]
    public void An_absurd_page_number_cannot_overflow_the_skip_offset()
    {
        var request = new PageRequest(int.MaxValue, PageRequest.MaxPageSize);
        Assert.True(request.Skip >= 0);
    }
}
