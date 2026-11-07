# syntax=docker/dockerfile:1
# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
# USER $APP_UID  # Commented out to allow reading Docker secrets from /run/secrets
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# QuestPDF (SkiaSharp) requires the fontconfig and freetype native libraries on Linux.
# Fonts themselves are bundled with the app (Lato via the QuestPDF package, Noto Sans
# Arabic as an embedded resource) and environment fonts are disabled, so no system
# font packages are needed.
RUN apt-get update && apt-get install -y --no-install-recommends \
    libfontconfig1 \
    libfreetype6 \
    && rm -rf /var/lib/apt/lists/*


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# Restore layer: only the build inputs that affect restore, so it is cached until a package or project
# reference changes. Keep this list = the ProjectReference closure of src/Api/Api.csproj (a new module adds its
# project + .Contracts here; a missing entry only costs cache, the later "COPY . ." still builds).
COPY ["global.json", "nuget.config", "Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/Api/Api.csproj", "src/Api/"]
COPY ["src/Aspire/ServiceDefaults/ServiceDefaults.csproj", "src/Aspire/ServiceDefaults/"]
COPY ["src/Shared/Shared.Contracts/Shared.Contracts.csproj", "src/Shared/Shared.Contracts/"]
COPY ["src/Shared/Shared.Core/Shared.Core.csproj", "src/Shared/Shared.Core/"]
COPY ["src/Shared/Shared.Messaging/Shared.Messaging.csproj", "src/Shared/Shared.Messaging/"]
COPY ["src/Modules/Identity/Identity/Identity.csproj", "src/Modules/Identity/Identity/"]
COPY ["src/Modules/Identity/Identity.Contracts/Identity.Contracts.csproj", "src/Modules/Identity/Identity.Contracts/"]
COPY ["src/Modules/Identity/UserDirectory.Contracts/UserDirectory.Contracts.csproj", "src/Modules/Identity/UserDirectory.Contracts/"]
COPY ["src/Modules/Tenancy/Tenancy/Tenancy.csproj", "src/Modules/Tenancy/Tenancy/"]
COPY ["src/Modules/Tenancy/Tenancy.Contracts/Tenancy.Contracts.csproj", "src/Modules/Tenancy/Tenancy.Contracts/"]
COPY ["src/Modules/Attachment/Attachment/Attachment.csproj", "src/Modules/Attachment/Attachment/"]
COPY ["src/Modules/Attachment/Attachment.Contracts/Attachment.Contracts.csproj", "src/Modules/Attachment/Attachment.Contracts/"]
COPY ["src/Modules/Catalog/Catalog/Catalog.csproj", "src/Modules/Catalog/Catalog/"]
COPY ["src/Modules/Catalog/Catalog.Contracts/Catalog.Contracts.csproj", "src/Modules/Catalog/Catalog.Contracts/"]
COPY ["src/Modules/Subscriptions/Subscriptions/Subscriptions.csproj", "src/Modules/Subscriptions/Subscriptions/"]
COPY ["src/Modules/Subscriptions/Subscriptions.Contracts/Subscriptions.Contracts.csproj", "src/Modules/Subscriptions/Subscriptions.Contracts/"]
COPY ["src/Modules/Notification/Notification/Notification.csproj", "src/Modules/Notification/Notification/"]
COPY ["src/Modules/Notification/Notification.Contracts/Notification.Contracts.csproj", "src/Modules/Notification/Notification.Contracts/"]
COPY ["src/Modules/Webhooks/Webhooks/Webhooks.csproj", "src/Modules/Webhooks/Webhooks/"]
COPY ["src/Modules/Webhooks/Webhooks.Contracts/Webhooks.Contracts.csproj", "src/Modules/Webhooks/Webhooks.Contracts/"]
COPY ["src/Modules/DataTransfer/DataTransfer/DataTransfer.csproj", "src/Modules/DataTransfer/DataTransfer/"]
COPY ["src/Modules/DataTransfer/DataTransfer.Contracts/DataTransfer.Contracts.csproj", "src/Modules/DataTransfer/DataTransfer.Contracts/"]
RUN dotnet restore "./src/Api/Api.csproj"
COPY . .
WORKDIR "/src/src/Api"
RUN dotnet build "./Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
# Publish for the image's architecture only: native libraries (SkiaSharp ~12 MB each) for the other ~20 platforms
# would add ~190 MB. TARGETARCH is set by BuildKit (amd64 / arm64).
ARG TARGETARCH
RUN case "$TARGETARCH" in arm64) RID=linux-arm64 ;; *) RID=linux-x64 ;; esac && \
    dotnet publish "./Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false \
      -r "$RID" --self-contained false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Copy Docker-specific appsettings
COPY ["src/Api/appsettings.Docker.json", "."]

# Set environment to use Docker configuration
ENV ASPNETCORE_ENVIRONMENT=Docker

ENTRYPOINT ["dotnet", "Api.dll"]