FROM mcr.microsoft.com/dotnet/sdk:9.0 AS nuget
WORKDIR /nuget
COPY [".local_nugets/", "./"]

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
RUN mkdir -p /nuget
COPY --from=nuget /nuget/*.nupkg /nuget/
COPY ["LovgaBroker/LovgaBroker.csproj", "./LovgaBroker/"]
RUN dotnet nuget add source /nuget --name local
RUN dotnet restore "LovgaBroker/LovgaBroker.csproj"
COPY LovgaBroker/. ./LovgaBroker/
RUN dotnet build "LovgaBroker/LovgaBroker.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "LovgaBroker/LovgaBroker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:9.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "LovgaBroker.dll"]