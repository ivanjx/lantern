FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build

RUN apt-get update \
    && apt-get install --yes --no-install-recommends clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir /data

WORKDIR /source

COPY Lantern/Lantern.csproj Lantern/
RUN dotnet restore Lantern/Lantern.csproj

COPY Lantern/ Lantern/
RUN dotnet publish Lantern/Lantern.csproj \
    --configuration Release \
    --no-restore \
    --output /app

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled-extra AS final

WORKDIR /app
COPY --from=build --chown=$APP_UID:$APP_UID /app/ ./
COPY --from=build --chown=$APP_UID:$APP_UID /data /data

ENV DATABASE_PATH=/data/lantern.db

USER $APP_UID
EXPOSE 8080
VOLUME /data

ENTRYPOINT ["./Lantern"]
