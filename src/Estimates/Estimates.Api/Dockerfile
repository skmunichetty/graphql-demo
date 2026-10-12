# Build context: repository root.  docker build -f src/Estimates/Estimates.Api/Dockerfile .
# Builds ONLY the Estimates service (and the shared technical defaults). No Accounting code is copied.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Restore first, using only project files, so the restore layer is cached until dependencies change.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ServiceDefaults/ServiceDefaults.csproj src/ServiceDefaults/
COPY src/Estimates/Estimates.Domain/Estimates.Domain.csproj src/Estimates/Estimates.Domain/
COPY src/Estimates/Estimates.Application/Estimates.Application.csproj src/Estimates/Estimates.Application/
COPY src/Estimates/Estimates.Infrastructure/Estimates.Infrastructure.csproj src/Estimates/Estimates.Infrastructure/
COPY src/Estimates/Estimates.Api/Estimates.Api.csproj src/Estimates/Estimates.Api/
RUN dotnet restore src/Estimates/Estimates.Api/Estimates.Api.csproj

COPY src/ServiceDefaults/ src/ServiceDefaults/
COPY src/Estimates/ src/Estimates/
RUN dotnet publish src/Estimates/Estimates.Api/Estimates.Api.csproj \
    --configuration Release --no-restore --output /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Run as the image's built-in non-root user.
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Estimates.Api.dll"]
