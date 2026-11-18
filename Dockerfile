# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copy solution and project files and restore dependencies
COPY PatientManagement.slnx ./
COPY src/PatientManagement.Core/*.csproj ./src/PatientManagement.Core/
COPY src/PatientManagement.Infrastructure/*.csproj ./src/PatientManagement.Infrastructure/
COPY src/PatientManagement.Api/*.csproj ./src/PatientManagement.Api/
COPY tests/PatientManagement.Tests/*.csproj ./tests/PatientManagement.Tests/

RUN dotnet restore ./src/PatientManagement.Api/PatientManagement.Api.csproj

# Copy the rest of the source code
COPY src/ ./src/
COPY prompts/ ./prompts/

# Build and Publish
WORKDIR /app/src/PatientManagement.Api
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build --chown=app:app /app/publish .
USER app
ENTRYPOINT ["dotnet", "PatientManagement.Api.dll"]
