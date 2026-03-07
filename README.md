# Fennath

> *"Doorways"* in Sindarin — a TLS-terminating reverse proxy for your homelab.

Fennath sits on the edge of your home network, accepting HTTPS traffic from the internet
and forwarding it as plain HTTP to your backend services. It handles TLS certificates,
DNS records, and route discovery so your toy projects can stay simple.

## Features

- **Reverse proxy with TLS termination** — backends run plain HTTP, Fennath handles HTTPS
- **Automatic Let's Encrypt certificates** — wildcard certs via DNS-01 challenge, zero manual renewal
- **DNS management via Loopia API** — automatic A record updates when your public IP changes
- **Route discovery** — static config (appsettings.json), plus optional Docker label auto-discovery
- **Full observability** — OpenTelemetry traces, metrics, and logs to Grafana Cloud

## Quick Start

```bash
# Clone and configure
cp appsettings.example.json appsettings.local.json
# Edit appsettings.local.json with your domain, Loopia credentials, and backend services

# Run with Docker Compose
docker compose up -d
```

## Configuration

Configuration uses the standard .NET configuration system. Copy
[`appsettings.example.json`](appsettings.example.json) to `appsettings.local.json`
(gitignored) and edit for your environment.

```json
{
  "Fennath": {
    "Domain": "example.com",
    "Routes": [
      { "Subdomain": "grafana", "Backend": "http://localhost:3000" },
      { "Subdomain": "git", "Backend": "http://192.168.1.50:3000" }
    ]
  }
}
```

Sensitive values (API passwords, OTel tokens) use environment variables:
```bash
export Fennath__Dns__Loopia__Password=your-api-password
export Fennath__Telemetry__Headers__Authorization="Basic ..."
```

### Docker Label Discovery

Optionally, Fennath can auto-discover routes from running Docker containers:

```bash
docker run -d \
  --label fennath.enable=true \
  --label fennath.subdomain=myapp \
  --label fennath.port=8080 \
  my-app:latest
```

## Architecture

Fennath is built with .NET 10 and [YARP](https://github.com/microsoft/reverse-proxy)
(Yet Another Reverse Proxy). Certificates are managed via [Certes](https://github.com/fszlin/certes)
(ACME v2 client) and DNS records via Loopia's XML-RPC API.

See [`docs/adr/`](docs/adr/) for Architecture Decision Records explaining key design choices,
and [`docs/implementation-plan.md`](docs/implementation-plan.md) for the phased build plan.

## Project Status

🚧 **Early development** — not yet functional. See the implementation plan for current progress.

## License

TBD
