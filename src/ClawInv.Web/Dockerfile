# Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore ./ClawInv.sln
RUN dotnet publish ./src/ClawInv.Web/ClawInv.Web.csproj -c Release -o /app/publish

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
COPY --from=build /app/publish .

# Persist database in /data
ENV ClawInv__DbPath=/data/clawinv.db
VOLUME ["/data"]

ENTRYPOINT ["dotnet", "ClawInv.Web.dll"]
