# Development image tags authorized by Technical Constraints; pin a tag or digest for production releases.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY .editorconfig Directory.Build.props Directory.Packages.props Authentication.slnx ./
COPY src/Authentication.Domain/Authentication.Domain.csproj src/Authentication.Domain/
COPY src/Authentication.Application/Authentication.Application.csproj src/Authentication.Application/
COPY src/Authentication.Infrastructure/Authentication.Infrastructure.csproj src/Authentication.Infrastructure/
COPY src/Authentication.Api/Authentication.Api.csproj src/Authentication.Api/
RUN dotnet restore src/Authentication.Api/Authentication.Api.csproj

COPY src/ src/
RUN dotnet publish src/Authentication.Api/Authentication.Api.csproj \
    --configuration Release --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# The official image ships a non-root "app" user; its UID is exposed as APP_UID.
USER $APP_UID
ENTRYPOINT ["dotnet", "Authentication.Api.dll"]
