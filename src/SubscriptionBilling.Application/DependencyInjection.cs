using Microsoft.Extensions.DependencyInjection;
using SubscriptionBilling.Application.Auth;
using SubscriptionBilling.Application.Plans;
using SubscriptionBilling.Application.Subscriptions;

namespace SubscriptionBilling.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        services.AddScoped<PlanService>();
        services.AddScoped<SubscriptionService>();
        return services;
    }
}
