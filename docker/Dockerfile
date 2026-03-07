FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first for layer caching
COPY Directory.Build.props Directory.Packages.props fennath.slnx global.json ./
COPY src/Fennath/Fennath.csproj src/Fennath/
RUN dotnet restore src/Fennath/Fennath.csproj

# Copy source and publish
COPY src/ src/
RUN dotnet publish src/Fennath/Fennath.csproj -c Release -o /app --no-restore

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN mkdir -p /data/certs && chown $APP_UID:$APP_UID /data/certs

COPY --from=build /app .

USER $APP_UID

EXPOSE 8080 8443

ENTRYPOINT ["dotnet", "Fennath.dll"]
