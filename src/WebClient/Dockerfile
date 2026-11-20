# Build context: thư mục gốc của repository
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/WebClient/WebClient.csproj src/WebClient/
RUN dotnet restore src/WebClient/WebClient.csproj
COPY src/WebClient/ src/WebClient/
RUN dotnet publish src/WebClient/WebClient.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "WebClient.dll"]
