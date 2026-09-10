FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER app
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ./src/*/*.*proj ./
RUN for file in $(ls *.*proj); do mkdir -p ./src/${file%.*}/ && mv $file ./src/${file%.*}/; done
COPY ["Directory.Build.props", "Directory.Build.props"]
# Versions live here, not on the PackageReference items. Without it every reference restores as versionless and
# the whole restore fails with NU1015.
COPY ["Directory.Packages.props", "Directory.Packages.props"]
COPY ["nuget.ci.config", "nuget.config"]
RUN dotnet restore "./src/Presentation/Presentation.csproj"
COPY . .
WORKDIR "/src/src/Presentation"
RUN dotnet build "./Presentation.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Presentation.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Presentation.dll"]
