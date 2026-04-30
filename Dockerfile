# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ComfyBridgeAPI.sln ./
COPY ComfyBridge.Api/ComfyBridge.Api.csproj ComfyBridge.Api/
COPY ComfyBridge.Application/ComfyBridge.Application.csproj ComfyBridge.Application/
COPY ComfyBridge.Domain/ComfyBridge.Domain.csproj ComfyBridge.Domain/
COPY ComfyBridge.Infrastructure/ComfyBridge.Infrastructure.csproj ComfyBridge.Infrastructure/

RUN dotnet restore ComfyBridgeAPI.sln

COPY . .
RUN dotnet publish ComfyBridge.Api/ComfyBridge.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ComfyBridge.Api.dll"]