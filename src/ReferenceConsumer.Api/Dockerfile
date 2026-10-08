# Development image tags authorized by Technical Constraints; pin a tag or digest for production releases.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY .editorconfig Directory.Build.props Directory.Packages.props ./
COPY src/ReferenceConsumer.Api/ReferenceConsumer.Api.csproj src/ReferenceConsumer.Api/
RUN dotnet restore src/ReferenceConsumer.Api/ReferenceConsumer.Api.csproj

COPY src/ReferenceConsumer.Api/ src/ReferenceConsumer.Api/
RUN dotnet publish src/ReferenceConsumer.Api/ReferenceConsumer.Api.csproj \
    --configuration Release --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# The official image ships a non-root "app" user; its UID is exposed as APP_UID.
USER $APP_UID
ENTRYPOINT ["dotnet", "ReferenceConsumer.Api.dll"]
