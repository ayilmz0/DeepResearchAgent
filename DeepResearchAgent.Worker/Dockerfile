FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore ./DeepResearchAgent.Worker/DeepResearchAgent.Worker.csproj
RUN dotnet publish ./DeepResearchAgent.Worker/DeepResearchAgent.Worker.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DeepResearchAgent.Worker.dll"]
