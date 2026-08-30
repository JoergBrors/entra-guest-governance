FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY b2b-guest-governance.sln ./
COPY Directory.Build.props ./
COPY global.json ./
COPY src/B2B.Portal.Worker/B2B.Portal.Worker.csproj src/B2B.Portal.Worker/
COPY src/B2B.Portal.Application/B2B.Portal.Application.csproj src/B2B.Portal.Application/
COPY src/B2B.Portal.Domain/B2B.Portal.Domain.csproj src/B2B.Portal.Domain/
COPY src/B2B.Portal.Infrastructure/B2B.Portal.Infrastructure.csproj src/B2B.Portal.Infrastructure/
RUN dotnet restore src/B2B.Portal.Worker/B2B.Portal.Worker.csproj

COPY src/ src/
RUN dotnet publish src/B2B.Portal.Worker/B2B.Portal.Worker.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "B2B.Portal.Worker.dll"]
