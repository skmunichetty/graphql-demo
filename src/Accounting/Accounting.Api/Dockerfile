# Build context: repository root.  docker build -f src/Accounting/Accounting.Api/Dockerfile .
# Builds ONLY the Accounting service (and the shared technical defaults). No Estimates code is copied.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ServiceDefaults/ServiceDefaults.csproj src/ServiceDefaults/
COPY src/Accounting/Accounting.Domain/Accounting.Domain.csproj src/Accounting/Accounting.Domain/
COPY src/Accounting/Accounting.Application/Accounting.Application.csproj src/Accounting/Accounting.Application/
COPY src/Accounting/Accounting.Infrastructure/Accounting.Infrastructure.csproj src/Accounting/Accounting.Infrastructure/
COPY src/Accounting/Accounting.Api/Accounting.Api.csproj src/Accounting/Accounting.Api/
RUN dotnet restore src/Accounting/Accounting.Api/Accounting.Api.csproj

COPY src/ServiceDefaults/ src/ServiceDefaults/
COPY src/Accounting/ src/Accounting/
RUN dotnet publish src/Accounting/Accounting.Api/Accounting.Api.csproj \
    --configuration Release --no-restore --output /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Accounting.Api.dll"]
