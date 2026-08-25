FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Meta.WhatsApp.Api/Meta.WhatsApp.Api.csproj -c Release -o /out --no-self-contained

FROM base AS final
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
ENTRYPOINT ["dotnet", "Meta.WhatsApp.Api.dll"]
