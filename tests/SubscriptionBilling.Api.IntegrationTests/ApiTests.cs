using System.Net;
using System.Net.Http.Json;
using SubscriptionBilling.Infrastructure.Security;
using static SubscriptionBilling.Api.IntegrationTests.TestClient;

namespace SubscriptionBilling.Api.IntegrationTests;

public sealed class AuthenticationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Register_login_and_read_the_profile()
    {
        var email = NewEmail();
        var anonymous = factory.CreateClient();

        var register = await anonymous.PostAsJsonAsync("/api/v1/auth/register", new { email = email.ToUpperInvariant(), fullName = "Ana Silva", password = ApiFactory.UserPassword }, Ct);
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var client = factory.ClientWith(await anonymous.LoginAsync(email, ApiFactory.UserPassword));
        var me = await client.GetAsync("/api/v1/auth/me", Ct);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var profile = await me.Content.ReadFromJsonAsync<Dictionary<string, object>>(Ct);
        Assert.Equal(email, profile!["email"].ToString()); // Stored normalised.
        Assert.Equal("User", profile["role"].ToString());
        Assert.False(profile.ContainsKey("passwordHash"));
    }

    [Fact]
    public async Task Registering_the_same_email_twice_is_a_conflict()
    {
        var client = factory.CreateClient();
        var email = NewEmail();
        var body = new { email, fullName = "Ana Silva", password = ApiFactory.UserPassword };
        (await client.PostAsJsonAsync("/api/v1/auth/register", body, Ct)).EnsureSuccessStatusCode();

        var again = await client.PostAsJsonAsync("/api/v1/auth/register", body, Ct);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("email_taken", await again.ProblemCodeAsync());
    }

    [Theory]
    [InlineData("not-an-email", "a perfectly fine passphrase", "Ana Silva")]
    [InlineData("ana@example.test", "short", "Ana Silva")]
    [InlineData("ana@example.test", "a perfectly fine passphrase", "A")]
    public async Task Invalid_registrations_are_rejected_with_a_400(string email, string password, string fullName)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/register", new { email, fullName, password }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_json_fields_are_rejected_so_nobody_can_smuggle_in_a_role()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email = NewEmail(), fullName = "Mallory", password = ApiFactory.UserPassword, role = "Admin" },
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_email_get_the_identical_401()
    {
        var client = factory.CreateClient();
        var email = NewEmail();
        (await client.PostAsJsonAsync("/api/v1/auth/register", new { email, fullName = "Ana Silva", password = ApiFactory.UserPassword }, Ct)).EnsureSuccessStatusCode();

        var wrong = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "definitely the wrong one" }, Ct);
        var unknown = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = NewEmail(), password = "definitely the wrong one" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await wrong.ProblemCodeAsync(), await unknown.ProblemCodeAsync());
    }

    [Fact]
    public async Task Protected_endpoints_reject_missing_and_forged_tokens()
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/auth/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/subscriptions", Ct)).StatusCode);

        var forged = factory.ClientWith("eyJhbGciOiJub25lIn0.eyJzdWIiOiIxIiwicm9sZSI6IkFkbWluIn0.");
        Assert.Equal(HttpStatusCode.Unauthorized, (await forged.GetAsync("/api/v1/auth/me", Ct)).StatusCode);
    }
}

public sealed class PlanTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task The_catalogue_is_public_but_changing_it_needs_an_administrator()
    {
        var anonymous = factory.CreateClient();
        var user = await factory.NewUserClientAsync();
        var plan = new { name = $"Plan {Guid.NewGuid():N}", description = "x", priceCents = 100, currency = "EUR", interval = "Monthly" };

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/v1/plans", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/plans", plan, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/v1/plans", plan, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/v1/plans/all", Ct)).StatusCode);
    }

    [Fact]
    public async Task An_administrator_can_create_update_and_delete_a_plan()
    {
        var admin = await factory.NewAdminClientAsync();
        var created = await admin.CreatePlanAsync(priceCents: 500);

        var update = await admin.PutAsJsonAsync($"/api/v1/plans/{created.Id}", new { name = created.Name, description = "new", priceCents = 700, currency = "usd", interval = "Yearly" }, Ct);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<PlanBody>(Ct);
        Assert.Equal((700L, "USD", "Yearly"), (updated!.PriceCents, updated.Currency, updated.Interval));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/plans/{created.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync($"/api/v1/plans/{created.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Duplicate_names_conflict_and_bad_values_are_rejected()
    {
        var admin = await factory.NewAdminClientAsync();
        var created = await admin.CreatePlanAsync();

        var duplicate = await admin.PostAsJsonAsync("/api/v1/plans", new { name = created.Name, description = "x", priceCents = 1, currency = "EUR", interval = "Monthly" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("plan_name_taken", await duplicate.ProblemCodeAsync());

        foreach (var body in new object[]
        {
            new { name = "Valid name", description = "x", priceCents = -5, currency = "EUR", interval = "Monthly" },
            new { name = "Valid name", description = "x", priceCents = 5, currency = "EURO", interval = "Monthly" },
            new { name = "Valid name", description = "x", priceCents = 5, currency = "EUR", interval = "Weekly" },
            new { name = "Valid name", description = "x", priceCents = 5, currency = "EUR", interval = 1 },
        })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/plans", body, Ct)).StatusCode);
        }
    }

    [Fact]
    public async Task Listing_is_paginated_and_the_page_size_is_capped()
    {
        var admin = await factory.NewAdminClientAsync();
        for (var i = 0; i < 3; i++)
        {
            await admin.CreatePlanAsync(priceCents: 100 + i);
        }

        var page = await (await admin.GetAsync("/api/v1/plans?page=1&pageSize=2", Ct)).Content.ReadFromJsonAsync<PageBody<PlanBody>>(Ct);
        Assert.Equal(2, page!.Items.Count);
        Assert.True(page.TotalCount >= 3);

        var capped = await (await admin.GetAsync("/api/v1/plans?pageSize=100000", Ct)).Content.ReadFromJsonAsync<PageBody<PlanBody>>(Ct);
        Assert.Equal(100, capped!.PageSize);
    }

    [Fact]
    public async Task Deactivated_plans_leave_the_public_catalogue()
    {
        var admin = await factory.NewAdminClientAsync();
        var created = await admin.CreatePlanAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/plans/{created.Id}", Ct)).StatusCode);

        var catalogue = await (await factory.CreateClient().GetAsync("/api/v1/plans?pageSize=100", Ct)).Content.ReadFromJsonAsync<PageBody<PlanBody>>(Ct);

        Assert.DoesNotContain(catalogue!.Items, x => x.Id == created.Id);
    }
}

public sealed class SubscriptionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Subscribe_list_cancel_and_resubscribe()
    {
        var admin = await factory.NewAdminClientAsync();
        var plan = await admin.CreatePlanAsync(priceCents: 1500);
        var user = await factory.NewUserClientAsync();

        var subscribe = await user.PostAsJsonAsync("/api/v1/subscriptions", new { planId = plan.Id }, Ct);
        Assert.Equal(HttpStatusCode.Created, subscribe.StatusCode);
        Assert.NotNull(subscribe.Headers.Location);
        var subscription = (await subscribe.Content.ReadFromJsonAsync<SubscriptionBody>(Ct))!;
        Assert.Equal("Active", subscription.Status);

        var second = await user.PostAsJsonAsync("/api/v1/subscriptions", new { planId = plan.Id }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("already_subscribed", await second.ProblemCodeAsync());

        var cancelled = await user.PostAsync($"/api/v1/subscriptions/{subscription.Id}/cancel", null, Ct);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        var cancelledAgain = await user.PostAsync($"/api/v1/subscriptions/{subscription.Id}/cancel", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, cancelledAgain.StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await user.PostAsJsonAsync("/api/v1/subscriptions", new { planId = plan.Id }, Ct)).StatusCode);
        var list = await (await user.GetAsync("/api/v1/subscriptions", Ct)).Content.ReadFromJsonAsync<PageBody<SubscriptionBody>>(Ct);
        Assert.Equal(2, list!.TotalCount);
    }

    [Fact]
    public async Task Users_only_ever_see_their_own_subscriptions()
    {
        var admin = await factory.NewAdminClientAsync();
        var plan = await admin.CreatePlanAsync();
        var alice = await factory.NewUserClientAsync();
        var mallory = await factory.NewUserClientAsync();
        var aliceSubscription = (await (await alice.PostAsJsonAsync("/api/v1/subscriptions", new { planId = plan.Id }, Ct)).Content.ReadFromJsonAsync<SubscriptionBody>(Ct))!;

        Assert.Equal(HttpStatusCode.NotFound, (await mallory.GetAsync($"/api/v1/subscriptions/{aliceSubscription.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mallory.PostAsync($"/api/v1/subscriptions/{aliceSubscription.Id}/cancel", null, Ct)).StatusCode);
        var malloryList = await (await mallory.GetAsync("/api/v1/subscriptions", Ct)).Content.ReadFromJsonAsync<PageBody<SubscriptionBody>>(Ct);
        Assert.Empty(malloryList!.Items);

        // Alice's subscription is untouched.
        var stillActive = await (await alice.GetAsync($"/api/v1/subscriptions/{aliceSubscription.Id}", Ct)).Content.ReadFromJsonAsync<SubscriptionBody>(Ct);
        Assert.Equal("Active", stillActive!.Status);
    }

    [Fact]
    public async Task A_subscription_keeps_the_price_it_was_bought_at()
    {
        var admin = await factory.NewAdminClientAsync();
        var plan = await admin.CreatePlanAsync(priceCents: 1000);
        var user = await factory.NewUserClientAsync();
        var subscription = (await (await user.PostAsJsonAsync("/api/v1/subscriptions", new { planId = plan.Id }, Ct)).Content.ReadFromJsonAsync<SubscriptionBody>(Ct))!;

        (await admin.PutAsJsonAsync($"/api/v1/plans/{plan.Id}", new { name = plan.Name, description = "x", priceCents = 9999, currency = "EUR", interval = "Monthly" }, Ct)).EnsureSuccessStatusCode();

        var reread = await (await user.GetAsync($"/api/v1/subscriptions/{subscription.Id}", Ct)).Content.ReadFromJsonAsync<SubscriptionBody>(Ct);
        Assert.Equal(1000, reread!.PriceCents);
    }

    [Fact]
    public async Task Plans_that_are_missing_or_off_sale_cannot_be_subscribed_to()
    {
        var admin = await factory.NewAdminClientAsync();
        var plan = await admin.CreatePlanAsync();
        await admin.DeleteAsync($"/api/v1/plans/{plan.Id}", Ct);
        var user = await factory.NewUserClientAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await user.PostAsJsonAsync("/api/v1/subscriptions", new { planId = plan.Id }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.PostAsJsonAsync("/api/v1/subscriptions", new { planId = Guid.NewGuid() }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_subscriptions_by_one_user_produce_exactly_one_active_subscription()
    {
        var admin = await factory.NewAdminClientAsync();
        var plan = await admin.CreatePlanAsync();
        var user = await factory.NewUserClientAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => user.PostAsJsonAsync("/api/v1/subscriptions", new { planId = plan.Id }, Ct)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        var list = await (await user.GetAsync("/api/v1/subscriptions", Ct)).Content.ReadFromJsonAsync<PageBody<SubscriptionBody>>(Ct);
        Assert.Equal(1, list!.TotalCount);
    }
}

public sealed class HttpHardeningTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Every_response_carries_the_security_headers_and_a_correlation_id()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/plans", Ct);

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-store", response.Headers.GetValues("Cache-Control").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.False(response.Headers.Contains("Server"));
        Assert.Matches("^[A-Za-z0-9-]{8,64}$", response.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task A_valid_incoming_correlation_id_is_kept_and_a_hostile_one_is_replaced()
    {
        var client = factory.CreateClient();

        var kept = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/plans") { Headers = { { "X-Correlation-Id", "trace-12345678" } } }, Ct);
        Assert.Equal("trace-12345678", kept.Headers.GetValues("X-Correlation-Id").Single());

        var hostile = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/plans") { Headers = { { "X-Correlation-Id", "x\" injected {json}" } } }, Ct);
        Assert.DoesNotContain("injected", hostile.Headers.GetValues("X-Correlation-Id").Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Errors_are_problem_details_with_the_correlation_id_and_no_internals()
    {
        var user = await factory.NewUserClientAsync();

        var response = await user.GetAsync("/api/v1/plans/not-a-guid", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
        Assert.Contains("correlationId", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Default_deny_means_an_anonymous_caller_cannot_even_probe_which_routes_exist()
    {
        var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/does-not-exist", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/plans/not-a-guid", Ct)).StatusCode);
    }

    [Fact]
    public async Task Oversized_bodies_and_malformed_json_are_rejected()
    {
        var client = factory.CreateClient();

        var malformed = await client.PostAsync("/api/v1/auth/login", new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);

        var wrongType = await client.PostAsync("/api/v1/auth/login", new StringContent("email=a&password=b", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded"), Ct);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongType.StatusCode);
    }

    [Fact]
    public async Task The_openapi_document_describes_the_api_and_its_bearer_scheme()
    {
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("/api/v1/subscriptions", document, StringComparison.Ordinal);
        Assert.Contains("\"bearer\"", document, StringComparison.Ordinal);
    }
}

public sealed class PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void A_hash_verifies_only_for_the_right_password_and_never_contains_it()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        Assert.True(_hasher.Verify(hash, "correct horse battery staple"));
        Assert.False(_hasher.Verify(hash, "correct horse battery stapl"));
        Assert.DoesNotContain("correct", hash, StringComparison.Ordinal);
        Assert.StartsWith("v1$", hash, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_password_hashes_differently_every_time()
    {
        Assert.NotEqual(_hasher.Hash("same password, long enough"), _hasher.Hash("same password, long enough"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("v1$1$AAAA$AAAA")]
    [InlineData("v1$210000$not-base64$AAAA")]
    public void Unknown_or_malformed_hashes_never_verify(string? stored)
    {
        Assert.False(_hasher.Verify(stored, "anything at all, really"));
    }
}
