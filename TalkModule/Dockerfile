FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["TalkModule/TalkModule.csproj", "TalkModule/"]
RUN dotnet restore "TalkModule/TalkModule.csproj"
COPY . .
WORKDIR "/src/TalkModule"
RUN dotnet build "TalkModule.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "TalkModule.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TalkModule.dll"]
