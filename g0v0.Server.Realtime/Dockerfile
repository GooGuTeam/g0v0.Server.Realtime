FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["g0v0.Server.Realtime/g0v0.Server.Realtime.csproj", "g0v0.Server.Realtime/"]
RUN dotnet restore "g0v0.Server.Realtime/g0v0.Server.Realtime.csproj"
COPY . .
WORKDIR "/src/g0v0.Server.Realtime"
RUN dotnet build "./g0v0.Server.Realtime.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./g0v0.Server.Realtime.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "g0v0.Server.Realtime.dll"]
