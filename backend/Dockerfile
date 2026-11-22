# syntax=docker/dockerfile:1
# Build context: ./backend  (podman build -f backend/Dockerfile backend)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore em camada própria: só muda quando os .csproj / pacotes mudam.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/FinMy.Domain/FinMy.Domain.csproj src/FinMy.Domain/
COPY src/FinMy.Application/FinMy.Application.csproj src/FinMy.Application/
COPY src/FinMy.Infrastructure/FinMy.Infrastructure.csproj src/FinMy.Infrastructure/
COPY src/FinMy.Api/FinMy.Api.csproj src/FinMy.Api/
RUN dotnet restore src/FinMy.Api/FinMy.Api.csproj

COPY src/ src/
RUN dotnet publish src/FinMy.Api/FinMy.Api.csproj \
    --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl é usado apenas pelo HEALTHCHECK (a imagem aspnet não o inclui).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Usuário não-root pré-criado na imagem aspnet (UID em $APP_UID).
USER $APP_UID

# O endpoint /health chega na T013; até lá o container aparece como unhealthy.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl --fail --silent http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "FinMy.Api.dll"]
