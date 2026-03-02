# Fortress Meeting Assistant — Web + API (combined)
# Standard MCR-based Dockerfile for AWS CodeBuild

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/RefugeMeetingAssistant.Web/RefugeMeetingAssistant.Web.csproj", "RefugeMeetingAssistant.Web/"]
COPY ["src/RefugeMeetingAssistant.Api/RefugeMeetingAssistant.Api.csproj", "RefugeMeetingAssistant.Api/"]
RUN dotnet restore "RefugeMeetingAssistant.Web/RefugeMeetingAssistant.Web.csproj"
COPY src/ .
WORKDIR "/src/RefugeMeetingAssistant.Web"
RUN dotnet publish "RefugeMeetingAssistant.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false /p:ErrorOnDuplicatePublishOutputFiles=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "RefugeMeetingAssistant.Web.dll"]
