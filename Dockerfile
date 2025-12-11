# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["WitcherHub/WitcherHub.csproj", "WitcherHub/"]
COPY ["WitcherHub.Application/WitcherHub.Application.csproj", "WitcherHub.Application/"]
COPY ["WitcherHub.Infrastructure/WitcherHub.Infrastructure.csproj", "WitcherHub.Infrastructure/"]
COPY ["WitcherHub.Domain/WitcherHub.Domain.csproj", "WitcherHub.Domain/"]
RUN dotnet restore "WitcherHub/WitcherHub.csproj"
COPY . .
WORKDIR "/src/WitcherHub"
RUN dotnet build "WitcherHub.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
WORKDIR /src/WitcherHub
RUN dotnet publish "WitcherHub.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app

COPY --from=publish /app/publish .
COPY WitcherHub/libs/linux/libwkhtmltox.so /app/libs/linux/libwkhtmltox.so


ENTRYPOINT ["dotnet", "WitcherHub.dll"]