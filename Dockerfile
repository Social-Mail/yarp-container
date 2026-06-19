# === Stage 1: Build & Publish ===
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /app

# Copy solution and project files first for caching layers
COPY ./*.sln ./
COPY ./DotNetReverseProxy/DotNetReverseProxy.csproj ./DotNetReverseProxy/
RUN dotnet restore ./DotNetReverseProxy/DotNetReverseProxy.csproj --no-cache

# Copy the rest of the source code
COPY . .

RUN dotnet publish ./DotNetReverseProxy/DotNetReverseProxy.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
LABEL org.opencontainers.image.source https://github.com/social-mail/yarp-container
WORKDIR /app
RUN apk add --no-cache libmsquic
COPY --from=build /app/publish .
EXPOSE 443/tcp
EXPOSE 443/udp
ENTRYPOINT [ "dotnet", "DotNetReverseProxy.dll" ]