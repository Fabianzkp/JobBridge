# Use .NET 9.0 runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

# Use .NET 9.0 SDK for building
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["JobBridge/JobBridge.csproj", "JobBridge/"]
RUN dotnet restore "JobBridge/JobBridge.csproj"
COPY . .
WORKDIR "/src/JobBridge"
RUN dotnet build "JobBridge.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "JobBridge.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "JobBridge.dll"]