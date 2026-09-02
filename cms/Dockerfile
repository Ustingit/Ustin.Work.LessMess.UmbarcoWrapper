# Umbraco 17 CMS image. Build context is the repo root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY cms/Ustin.Work.LessMess.UmbarcoWrapper.Cms.csproj cms/
RUN dotnet restore cms/Ustin.Work.LessMess.UmbarcoWrapper.Cms.csproj

COPY cms/ cms/
RUN dotnet publish cms/Ustin.Work.LessMess.UmbarcoWrapper.Cms.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl is only needed for the compose health check / debugging.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

RUN mkdir -p /app/umbraco/Data
VOLUME ["/app/umbraco/Data"]

ENTRYPOINT ["dotnet", "Ustin.Work.LessMess.UmbarcoWrapper.Cms.dll"]
