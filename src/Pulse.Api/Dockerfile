FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/Pulse.sln src/
COPY src/Pulse.Domain/ src/Pulse.Domain/
COPY src/Pulse.Api/ src/Pulse.Api/
COPY src/Pulse.Worker/ src/Pulse.Worker/
COPY tests/Pulse.Domain.Tests/ tests/Pulse.Domain.Tests/
RUN dotnet restore src/Pulse.sln \
    && dotnet publish src/Pulse.Api/Pulse.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Pulse.Api.dll"]
