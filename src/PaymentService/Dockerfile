# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copy project files and restore
COPY src/SharedEvents/SharedEvents.csproj src/SharedEvents/
COPY src/PaymentService/PaymentService.csproj src/PaymentService/
RUN dotnet restore src/PaymentService/PaymentService.csproj

# Copy source and publish
COPY src/SharedEvents/ src/SharedEvents/
COPY src/PaymentService/ src/PaymentService/
WORKDIR /app/src/PaymentService
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime stage (optimized for low memory)
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Set hard heap limit for 1GB RAM server (e.g. 80MB max GC heap)
ENV DOTNET_GCHeapHardLimit=0x5000000
ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080
ENTRYPOINT ["dotnet", "PaymentService.dll"]
