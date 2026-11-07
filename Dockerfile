# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copy solution and project files for caching restore layer
COPY EnUcuzTelefon.sln .
COPY nuget.config .
COPY Directory.Build.props .
COPY src/EnUcuzTelefon.Domain/*.csproj src/EnUcuzTelefon.Domain/
COPY src/EnUcuzTelefon.Shared/*.csproj src/EnUcuzTelefon.Shared/
COPY src/EnUcuzTelefon.Application/*.csproj src/EnUcuzTelefon.Application/
COPY src/EnUcuzTelefon.Infrastructure/*.csproj src/EnUcuzTelefon.Infrastructure/
COPY src/EnUcuzTelefon.Api/*.csproj src/EnUcuzTelefon.Api/
COPY src/EnUcuzTelefon.Worker/*.csproj src/EnUcuzTelefon.Worker/
COPY src/EnUcuzTelefon.Scheduler/*.csproj src/EnUcuzTelefon.Scheduler/
COPY tests/EnUcuzTelefon.UnitTests/*.csproj tests/EnUcuzTelefon.UnitTests/
COPY tests/EnUcuzTelefon.IntegrationTests/*.csproj tests/EnUcuzTelefon.IntegrationTests/
COPY tests/EnUcuzTelefon.ArchitectureTests/*.csproj tests/EnUcuzTelefon.ArchitectureTests/

RUN dotnet restore src/EnUcuzTelefon.Api/EnUcuzTelefon.Api.csproj

# Copy source and publish
COPY src/EnUcuzTelefon.Domain/ src/EnUcuzTelefon.Domain/
COPY src/EnUcuzTelefon.Shared/ src/EnUcuzTelefon.Shared/
COPY src/EnUcuzTelefon.Application/ src/EnUcuzTelefon.Application/
COPY src/EnUcuzTelefon.Infrastructure/ src/EnUcuzTelefon.Infrastructure/
COPY src/EnUcuzTelefon.Api/ src/EnUcuzTelefon.Api/

RUN dotnet publish src/EnUcuzTelefon.Api/EnUcuzTelefon.Api.csproj -c Release -o /out

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /out .

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 5000 8080

ENTRYPOINT ["dotnet", "EnUcuzTelefon.Api.dll"]
