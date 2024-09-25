FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["MS-Messenger-Test/MS-Messenger-Test.csproj", "MS-Messenger-Test/"]
RUN dotnet restore "MS-Messenger-Test/MS-Messenger-Test.csproj"
COPY . .
WORKDIR "/src/MS-Messenger-Test"
RUN dotnet build "MS-Messenger-Test.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "MS-Messenger-Test.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MS-Messenger-Test.dll"]
