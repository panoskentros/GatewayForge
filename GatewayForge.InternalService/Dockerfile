FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["GatewayForge.InternalService/GatewayForge.InternalService.csproj", "GatewayForge.InternalService/"]
COPY ["GatewayForge.Shared/GatewayForge.Shared.csproj", "GatewayForge.Shared/"]
RUN dotnet restore "GatewayForge.InternalService/GatewayForge.InternalService.csproj"

COPY GatewayForge.InternalService/ GatewayForge.InternalService/
COPY GatewayForge.Shared/ GatewayForge.Shared/
WORKDIR "/src/GatewayForge.InternalService"
RUN dotnet publish "GatewayForge.InternalService.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "GatewayForge.InternalService.dll"]
