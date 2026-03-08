<p align="center">
  <img src="assets/fennath-tengwar.svg" alt="'Fennath' in Tengwar" height="80">
</p>

**Add a Docker label to your container. Fennath does the rest** — provisions a
TLS certificate, creates DNS records, and starts routing HTTPS traffic to your
service. No manual certificate management, no DNS console, no proxy configuration.

Fennath sits on the edge of your home network as a TLS-terminating reverse proxy.
It handles Let's Encrypt certificates, Loopia DNS records, and route discovery so
your homelab services can stay simple.

> "Fennath" means "doorways" in Sindarin, the constructed language for Tolkien's elves.

## Features

- **Zero-touch HTTPS** — wildcard Let's Encrypt certs via DNS-01, automatic renewal
- **Automatic DNS** — A records created and updated when your public IP changes (Loopia API)
- **Docker label discovery** — add `fennath.subdomain=myapp` and you're live
- **TLS termination** — backends run plain HTTP, Fennath handles HTTPS
- **Full observability** — OpenTelemetry traces, metrics, and logs via OTLP
- **HTTP → HTTPS redirect** — automatic for all traffic

## Quick Start

### Prerequisites

- A domain managed by [Loopia](https://www.loopia.se/) with API credentials
- A Linux host with a public IP and ports 80/443 reachable from the internet
- Docker and Docker Compose installed

### 1. Clone and configure

```bash
git clone https://github.com/crhaglun/fennath.git
cd fennath
cp docker/.env.example docker/.env
```

Edit `docker/.env` with your domain, Loopia credentials, and certificate email.
See [`docker/.env.example`](docker/.env.example) for all available settings.

### 2. Deploy with Docker Compose

```bash
docker compose -f docker/docker-compose.yaml up -d
```

Fennath will:
1. Provision a wildcard TLS certificate from Let's Encrypt (via DNS-01 challenge)
2. Detect your public IP and create DNS A records via Loopia
3. Start proxying HTTPS traffic to your configured backends

### 3. Verify

```bash
curl -I https://grafana.yourdomain.com
```

## Configuration

All configuration is via environment variables in `docker/.env`. Copy
[`docker/.env.example`](docker/.env.example) to `docker/.env` (gitignored) and
edit for your environment.

Fennath uses the `Fennath__` prefix with `__` as section separator, following the
standard .NET configuration convention:

```bash
# Required
Fennath__Domain=example.com                          # Your registered domain
Fennath__Dns__Loopia__Username=user@loopiaapi
Fennath__Dns__Loopia__Password=your-api-password
Fennath__Certificates__Email=admin@example.com

# Optional — scope all services under a subdomain prefix
# Fennath__Subdomain=lab    # → services at *.lab.example.com
```

See `docker/.env.example` for the full list of settings including intervals,
logging levels, and OpenTelemetry configuration.

### Docker Label Discovery

Fennath discovers routes from running Docker containers via labels:

```bash
docker run -d \
  --label fennath.subdomain=myapp \
  --label fennath.port=8080 \
  my-app:latest
```

The backend URL is derived from the container name and port (`http://{container_name}:{port}`).
The `fennath.port` label defaults to 80 if omitted.

### Staging Certificates

For testing, set `Fennath__Certificates__Staging=true` in your `.env` to use
Let's Encrypt's staging environment (avoids rate limits, but certificates are
not browser-trusted).

## Development

```bash
# Build
dotnet build

# Run locally
dotnet run --project src/Fennath/

# Run tests
dotnet test
```

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

For local development without Docker, copy `src/Fennath/appsettings.example.json`
to `appsettings.local.json` (gitignored) and edit for your environment.

## Architecture

Fennath is built with .NET 10 and [YARP](https://github.com/microsoft/reverse-proxy)
(Yet Another Reverse Proxy). Certificates are managed via [Certes](https://github.com/fszlin/certes)
(ACME v2 client) and DNS records via Loopia's XML-RPC API.

See [`docs/adr/`](docs/adr/) for Architecture Decision Records explaining key design choices.

## License

[MIT](LICENSE)
