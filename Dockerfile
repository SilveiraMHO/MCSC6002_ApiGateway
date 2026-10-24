# Build with the SDK; restore takes AuthLibrary and ResponseLibrary from nuget-local only (nuget.config).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish ApiGateway/ApiGateway.csproj -c Release -o /app

# Run on the ASP.NET runtime only, as the image's non-root user. Ports, certificates and secrets come from docker-compose.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "ApiGateway.dll"]
