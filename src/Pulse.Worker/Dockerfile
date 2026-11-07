FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/ src/
COPY tests/ tests/
RUN dotnet restore src/Pulse.sln \
    && dotnet publish src/Pulse.Worker/Pulse.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Pulse.Worker.dll"]
