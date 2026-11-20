# Build context: thư mục gốc của repository
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/Auction.Contracts/Auction.Contracts.csproj src/Auction.Contracts/
COPY src/Audit.Worker/Audit.Worker.csproj src/Audit.Worker/
RUN dotnet restore src/Audit.Worker/Audit.Worker.csproj
COPY src/Auction.Contracts/ src/Auction.Contracts/
COPY src/Audit.Worker/ src/Audit.Worker/
RUN dotnet publish src/Audit.Worker/Audit.Worker.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Audit.Worker.dll"]
