FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build

ARG TARGETARCH

COPY . /source

WORKDIR /source

RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish -a ${TARGETARCH/amd64/x64} --use-current-runtime --self-contained false -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final

WORKDIR /app

COPY --from=build /app .

ARG UID=10001

RUN adduser \
    -D \
    -g "" \
    -h "/nonexistent" \
    -s "/sbin/nologin" \
    -H \
    -u "${UID}" \
    appuser
    
USER appuser

ENTRYPOINT ["dotnet", "docker-image-updater.dll"]