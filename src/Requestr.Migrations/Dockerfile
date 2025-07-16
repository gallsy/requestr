FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy migration project files
COPY src/Requestr.Migrations/Requestr.Migrations.csproj src/Requestr.Migrations/
RUN dotnet restore src/Requestr.Migrations/Requestr.Migrations.csproj

# Copy the migration scripts and source code
COPY src/Requestr.Migrations/ src/Requestr.Migrations/

# Build the migrations project
RUN dotnet build src/Requestr.Migrations/Requestr.Migrations.csproj -c Release -o /app/build

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/build .

# Default entrypoint
ENTRYPOINT ["dotnet", "Requestr.Migrations.dll"]
