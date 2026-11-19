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


# Every *.csproj with its path, nothing else. This stage re-runs on any source change, but its output only changes
# when a project file does, so the restore layer below stays cached. It also needs no edits when a module is added
# (Api.csproj references modules by glob, ADR-0019).
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS projects
WORKDIR /src
COPY . .
RUN mkdir /projects && find . -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | tar --null -cf - -T - | tar -xf - -C /projects

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# Restore layer: only the build inputs that affect restore.
COPY ["global.json", "nuget.config", "Directory.Build.props", "Directory.Packages.props", "./"]
COPY --from=projects /projects/ ./
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