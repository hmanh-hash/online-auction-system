# Build context: thư mục gốc của repository
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/Auction.Contracts/Auction.Contracts.csproj src/Auction.Contracts/
COPY src/BidValidation.Service/BidValidation.Service.csproj src/BidValidation.Service/
RUN dotnet restore src/BidValidation.Service/BidValidation.Service.csproj
COPY src/Auction.Contracts/ src/Auction.Contracts/
COPY src/BidValidation.Service/ src/BidValidation.Service/
RUN dotnet publish src/BidValidation.Service/BidValidation.Service.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=5200
EXPOSE 5200
ENTRYPOINT ["dotnet", "BidValidation.Service.dll"]
