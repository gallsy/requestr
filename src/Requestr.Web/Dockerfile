FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/Requestr.Web/Requestr.Web.csproj", "src/Requestr.Web/"]
COPY ["src/Requestr.Core/Requestr.Core.csproj", "src/Requestr.Core/"]
RUN dotnet restore "src/Requestr.Web/Requestr.Web.csproj"
COPY . .
WORKDIR "/src/src/Requestr.Web"
RUN dotnet build "Requestr.Web.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Requestr.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Requestr.Web.dll"]
