# Refuge Meeting Assistant — .NET API
# Uses Debian base with .NET 8 from Microsoft apt repo (avoids MCR pull)

# ---- Build stage ----
FROM debian:bookworm-slim AS build

# Install .NET SDK 8.0
RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates curl apt-transport-https && \
    curl -sSL https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb -o packages-microsoft-prod.deb && \
    dpkg -i packages-microsoft-prod.deb && rm packages-microsoft-prod.deb && \
    apt-get update && apt-get install -y --no-install-recommends dotnet-sdk-8.0 && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /src
COPY ["RefugeMeetingAssistant.Api.csproj", "./"]
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

# ---- Runtime stage ----
FROM debian:bookworm-slim AS final

# Install ASP.NET Core Runtime 8.0
RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates curl apt-transport-https && \
    curl -sSL https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb -o packages-microsoft-prod.deb && \
    dpkg -i packages-microsoft-prod.deb && rm packages-microsoft-prod.deb && \
    apt-get update && apt-get install -y --no-install-recommends aspnetcore-runtime-8.0 && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app
EXPOSE 5000
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "RefugeMeetingAssistant.Api.dll"]
