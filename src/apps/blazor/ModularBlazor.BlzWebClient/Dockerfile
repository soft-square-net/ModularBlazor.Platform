# The published Blazor client: static files served by nginx. Build context: the repository root
# (the client builds against the platform's projects). Used by the Hybird app host when publishing (Deploy.AddClient):
#   docker build -f src/apps/blazor/ModularBlazor.BlzWebClient/Dockerfile -t modularblazor-client .
# At start, deploy/40-appsettings.sh writes the public URLs into appsettings.json (OIDC_AUTHORITY, WEBAPI_BASE_URL).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY . .
RUN dotnet publish src/apps/blazor/ModularBlazor.BlzWebClient/ModularBlazor.BlzWebClient.csproj -c Release -o /out

FROM nginx:1.29-alpine
COPY src/apps/blazor/ModularBlazor.BlzWebClient/deploy/nginx.conf /etc/nginx/conf.d/default.conf
COPY --chmod=755 src/apps/blazor/ModularBlazor.BlzWebClient/deploy/40-appsettings.sh /docker-entrypoint.d/40-appsettings.sh
COPY --from=build /out/wwwroot /usr/share/nginx/html
EXPOSE 8080
