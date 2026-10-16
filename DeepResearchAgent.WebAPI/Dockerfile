FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore ./DeepResearchAgent.WebAPI/DeepResearchAgent.WebAPI.csproj
RUN dotnet publish ./DeepResearchAgent.WebAPI/DeepResearchAgent.WebAPI.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DeepResearchAgent.WebAPI.dll"]
