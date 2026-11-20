# Build context: thư mục gốc của repository
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/Auction.Contracts/Auction.Contracts.csproj src/Auction.Contracts/
COPY src/Auction.API/Auction.API.csproj src/Auction.API/
RUN dotnet restore src/Auction.API/Auction.API.csproj
COPY src/Auction.Contracts/ src/Auction.Contracts/
COPY src/Auction.API/ src/Auction.API/
RUN dotnet publish src/Auction.API/Auction.API.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Auction.API.dll"]
