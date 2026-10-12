FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["GatewayForge.Gateway/GatewayForge.Gateway.csproj", "GatewayForge.Gateway/"]
COPY ["GatewayForge.Shared/GatewayForge.Shared.csproj", "GatewayForge.Shared/"]
RUN dotnet restore "GatewayForge.Gateway/GatewayForge.Gateway.csproj"

COPY GatewayForge.Gateway/ GatewayForge.Gateway/
COPY GatewayForge.Shared/ GatewayForge.Shared/
WORKDIR "/src/GatewayForge.Gateway"
RUN dotnet publish "GatewayForge.Gateway.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "GatewayForge.Gateway.dll"]
