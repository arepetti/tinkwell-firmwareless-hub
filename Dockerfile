FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first for layer caching
COPY Directory.Build.props Directory.Packages.props Tinkwell.Firmwareless.Hub.slnx ./
COPY proto/ proto/
COPY src/Tinkwell.Firmwareless.Hub/Tinkwell.Firmwareless.Hub.csproj src/Tinkwell.Firmwareless.Hub/
COPY src/Tinkwell.Firmwareless.Hub.Host/Tinkwell.Firmwareless.Hub.Host.csproj src/Tinkwell.Firmwareless.Hub.Host/

# NOTE: In a real build, referenced projects (Tinkwell.Coap, Tinkwell.Package,
# Registry.Client) would also be copied here. For the prototype, we publish
# self-contained from the repo root.

RUN dotnet restore

COPY . .
RUN dotnet publish src/Tinkwell.Firmwareless.Hub -c Release -o /app/coordinator
RUN dotnet publish src/Tinkwell.Firmwareless.Hub.Host -c Release -o /app/host

# ── Runtime image ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/runtime:10.0

# TODO: Add WAMR native libraries (libiwasm.so) when available.
# RUN apt-get update && apt-get install -y --no-install-recommends libiwasm && rm -rf /var/lib/apt/lists/*

RUN groupadd -r tinkwell && useradd -r -g tinkwell -m tinkwell

WORKDIR /app
COPY --from=build /app/coordinator ./coordinator/
COPY --from=build /app/host ./host/
COPY config/ ./config/

RUN mkdir -p /var/lib/tinkwell/hub/assets.d /var/lib/tinkwell/hub/firmlets \
    && chown -R tinkwell:tinkwell /var/lib/tinkwell

ENV TW_HUB_DATA_DIR=/var/lib/tinkwell/hub
ENV Hub__HostExePath=/app/host/Tinkwell.Firmwareless.Hub.Host

EXPOSE 5684/udp

USER tinkwell

ENTRYPOINT ["dotnet", "/app/coordinator/Tinkwell.Firmwareless.Hub.dll"]
