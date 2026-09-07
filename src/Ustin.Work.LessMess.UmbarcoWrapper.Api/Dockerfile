# API image. Build context is the repo root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Restore against the whole src/ tree (Directory.Build.props / Directory.Packages.props
# plus the Core / Infrastructure / Api / Worker project files).
COPY src/ src/
RUN dotnet restore src/Ustin.Work.LessMess.UmbarcoWrapper.Api/Ustin.Work.LessMess.UmbarcoWrapper.Api.csproj

RUN dotnet publish src/Ustin.Work.LessMess.UmbarcoWrapper.Api/Ustin.Work.LessMess.UmbarcoWrapper.Api.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Wrapper__DataDirectory=/data
EXPOSE 8080

RUN mkdir -p /data
VOLUME ["/data"]

HEALTHCHECK --interval=10s --timeout=5s --retries=10 --start-period=15s \
    CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Ustin.Work.LessMess.UmbarcoWrapper.Api.dll"]
