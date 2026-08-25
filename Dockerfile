FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build

WORKDIR /source

COPY src/Beeexy.Api/Beeexy.Api.csproj src/Beeexy.Api/
COPY src/Beeexy.Application/Beeexy.Application.csproj src/Beeexy.Application/
COPY src/Beeexy.Domain/Beeexy.Domain.csproj src/Beeexy.Domain/
COPY src/Beeexy.Infrastructure/Beeexy.Infrastructure.csproj src/Beeexy.Infrastructure/

RUN dotnet restore src/Beeexy.Api/Beeexy.Api.csproj

COPY src/ src/

RUN dotnet publish src/Beeexy.Api/Beeexy.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS final

WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    ASPNETCORE_HTTP_PORTS= \
    DOTNET_EnableDiagnostics=0

COPY --from=build --chown=app:app /app/publish .

# Beeexy's current FHIR artifact store writes here. The directory remains usable
# without a disk, while Render can mount persistent storage at this exact path.
RUN mkdir -p /app/private-fhir-artifacts \
    && chown app:app /app/private-fhir-artifacts

USER app

CMD ["sh", "-c", "test -n \"$PORT\" || { echo 'PORT is required' >&2; exit 1; }; exec dotnet Beeexy.Api.dll --urls \"http://0.0.0.0:${PORT}\""]
