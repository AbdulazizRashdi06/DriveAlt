# DriveAlt: one image with the API, the built SPA and the converters (LibreOffice, poppler, libheif).
# Build from the repo root:  docker build -f deploy/Dockerfile -t drivealt .

FROM node:22-bookworm-slim AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npx tsc -b && npx vite build --outDir /out/wwwroot --emptyOutDir

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY src/DriveAlt.Api/DriveAlt.Api.csproj src/DriveAlt.Api/
RUN dotnet restore src/DriveAlt.Api/DriveAlt.Api.csproj
COPY src/ src/
RUN dotnet publish src/DriveAlt.Api -c Release -o /out/app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      libreoffice-impress libreoffice-writer poppler-utils libheif-examples \
      fonts-dejavu-core fonts-liberation fonts-noto-core \
 && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /data && chown app:app /data
WORKDIR /app
COPY --from=api /out/app ./
COPY --from=web /out/wwwroot ./wwwroot
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Storage__Root=/data \
    HOME=/tmp
USER app
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "DriveAlt.Api.dll"]
