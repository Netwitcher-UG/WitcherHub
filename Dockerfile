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

RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates \
    libglib2.0-0t64 libnss3 libnspr4 libatk1.0-0t64 libatk-bridge2.0-0 libcups2t64 \
    libdrm2 libdbus-1-3 libxkbcommon0 libgbm1 libasound2t64 libatspi2.0-0 \
    libx11-6 libxcomposite1 libxdamage1 libxext6 libxfixes3 libxrandr2 libxrender1 \
    libpango-1.0-0 libcairo2 libgtk-3-0 \
    libfontconfig1 libfreetype6 \
    libgssapi-krb5-2 libkrb5-3 \
    && rm -rf /var/lib/apt/lists/*
    

COPY --from=publish /app/publish .
COPY WitcherHub/libs/linux/libwkhtmltox.so /app/libs/linux/libwkhtmltox.so

ENV LD_LIBRARY_PATH="/app/libs/linux:${LD_LIBRARY_PATH}"

# Chromium, baked into the image.
#
# The shared libraries above are Chromium's dependencies — they were installed
# and Chromium itself never was. Nothing in this repository put a browser in the
# image and PLAYWRIGHT_BROWSERS_PATH was never set, so the first request for a
# PDF downloaded a browser from Playwright's CDN into the running container. On
# a platform with a read-only or ephemeral filesystem, restricted egress, or a
# non-root runtime user, that download fails — and the failure surfaced as an
# unexplained HTTP 500 on the PDF button.
#
# Downloading it here instead makes it part of the image: no network at request
# time, no first-PDF-after-deploy delay, and no repeat after every restart.
#
# Installed with the CLI that ships inside the published output, so this needs
# no PowerShell and no global tool. The path is fixed rather than $HOME-relative
# so it does not depend on which user the container ends up running as, and it
# is made world-readable for the same reason.
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright

RUN /app/.playwright/node/linux-x64/node /app/.playwright/package/cli.js install chromium \
    && chmod -R a+rX /ms-playwright

ENTRYPOINT ["dotnet", "WitcherHub.dll"]