FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Meta.WhatsApp.Worker/Meta.WhatsApp.Worker.csproj -c Release -o /out --no-self-contained

FROM base AS final
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
ENTRYPOINT ["dotnet", "Meta.WhatsApp.Worker.dll"]
