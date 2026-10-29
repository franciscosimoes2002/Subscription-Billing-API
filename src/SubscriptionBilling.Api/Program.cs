using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using SubscriptionBilling.Api.Http;
using SubscriptionBilling.Api.Options;
using SubscriptionBilling.Api.Security;
using SubscriptionBilling.Api.Startup;
using SubscriptionBilling.Application;
using SubscriptionBilling.Application.Abstractions;
using SubscriptionBilling.Domain.Users;
using SubscriptionBilling.Infrastructure;
using SubscriptionBilling.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.AddServerHeader = false; // Do not advertise the server software.
    kestrel.Limits.MaxRequestBodySize = 64 * 1024; // Every payload of this API is a few hundred bytes.
});

if (!builder.Environment.IsDevelopment())
{
    // One JSON document per log event, scopes included: ready for any log pipeline (and for the correlation id).
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
}

// ---- Configuration: validated at startup, so a missing secret fails the deploy instead of the first request.
builder.Services.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<DatabaseOptions>().BindConfiguration(DatabaseOptions.SectionName);
builder.Services.AddOptions<SeedOptions>().BindConfiguration(SeedOptions.SectionName);

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:Default is not configured. Provide it through the environment (ConnectionStrings__Default) or user-secrets.");
}

// ---- Application and infrastructure.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ITokenService, JwtTokenService>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(connectionString);

// ---- Authentication and authorization. Default-deny: an endpoint is public only if it says [AllowAnonymous].
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
    {
        bearer.MapInboundClaims = false; // Keep the claim names we issue ("sub", "role") instead of the legacy WS-* URIs.
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Value.Issuer,
            ValidAudience = jwt.Value.Audience,
            IssuerSigningKey = JwtTokenService.CreateKey(jwt.Value.SigningKey),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = JwtTokenService.RoleClaim,
        };
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(Policies.Admin, policy => policy.RequireRole(nameof(UserRole.Admin)));

// ---- HTTP surface.
builder.Services.AddControllers().AddJsonOptions(json =>
{
    json.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
    json.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow; // Unknown fields are rejected, not ignored.
    json.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    if (context.HttpContext.Items[CorrelationIdMiddleware.ItemKey] is string correlationId)
    {
        context.ProblemDetails.Extensions["correlationId"] = correlationId;
    }
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "Subscription Billing API",
            Version = "v1",
            Description = "Users, subscription plans and billing for a SaaS-style product. Authenticate with POST /api/v1/auth/login and send the token as a Bearer credential.",
        };
        document.Components ??= new OpenApiComponents();
        document.AddComponent("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" });
        return Task.CompletedTask;
    });
    options.AddOperationTransformer((operation, context, _) =>
    {
        // Mirror the real authorization rule: everything needs a token unless the endpoint is [AllowAnonymous].
        var anonymous = context.Description.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any();
        if (!anonymous && context.Document is not null)
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = [] });
        }

        return Task.CompletedTask;
    });
});

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseApiSecurityHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("OpenApi:Enabled"))
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapControllers();

// Liveness: the process answers. Readiness: the database is reachable. Neither says why it is not ready.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapGet("/ready", async (AppDbContext db, CancellationToken cancellationToken) =>
    await db.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ready" })
        : Results.Json(new { status = "not_ready" }, statusCode: StatusCodes.Status503ServiceUnavailable)).AllowAnonymous();

await DatabaseInitializer.RunAsync(app.Services);
await app.RunAsync();

// Exposed for WebApplicationFactory in the integration tests.
public partial class Program;
