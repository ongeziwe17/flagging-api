# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore as distinct layers
COPY FeatureFlags.Api.csproj ./
RUN dotnet restore "FeatureFlags.Api.csproj"

# Copy the rest of the source and publish
COPY . .
RUN dotnet publish "FeatureFlags.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS="http://0.0.0.0:8080"
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "FeatureFlags.Api.dll"]

