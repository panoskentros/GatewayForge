FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["GatewayForge.LoadTest/GatewayForge.LoadTest.csproj", "GatewayForge.LoadTest/"]
RUN dotnet restore "GatewayForge.LoadTest/GatewayForge.LoadTest.csproj"
COPY GatewayForge.LoadTest/ GatewayForge.LoadTest/
RUN dotnet publish "GatewayForge.LoadTest/GatewayForge.LoadTest.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "GatewayForge.LoadTest.dll"]
