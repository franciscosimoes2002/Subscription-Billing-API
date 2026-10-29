# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first: the restore layer is cached until a package or project reference changes.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/SubscriptionBilling.Domain/*.csproj src/SubscriptionBilling.Domain/
COPY src/SubscriptionBilling.Application/*.csproj src/SubscriptionBilling.Application/
COPY src/SubscriptionBilling.Infrastructure/*.csproj src/SubscriptionBilling.Infrastructure/
COPY src/SubscriptionBilling.Api/*.csproj src/SubscriptionBilling.Api/
RUN dotnet restore src/SubscriptionBilling.Api

COPY src/ src/
RUN dotnet publish src/SubscriptionBilling.Api --no-restore -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# The image ships a non-root `app` user; the API listens on 8080, so it needs no privileges.
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "SubscriptionBilling.Api.dll"]
