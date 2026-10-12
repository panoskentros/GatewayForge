FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["GatewayForge.SecureService/GatewayForge.SecureService.csproj", "GatewayForge.SecureService/"]
COPY ["GatewayForge.Shared/GatewayForge.Shared.csproj", "GatewayForge.Shared/"]
RUN dotnet restore "GatewayForge.SecureService/GatewayForge.SecureService.csproj"

COPY GatewayForge.SecureService/ GatewayForge.SecureService/
COPY GatewayForge.Shared/ GatewayForge.Shared/
WORKDIR "/src/GatewayForge.SecureService"
RUN dotnet publish "GatewayForge.SecureService.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "GatewayForge.SecureService.dll"]
