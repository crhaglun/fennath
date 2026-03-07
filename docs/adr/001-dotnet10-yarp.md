# ADR-001: .NET 10 with YARP as Reverse Proxy

**Status:** Accepted  
**Date:** 2026-03-07

## Context

Fennath is a TLS-terminating reverse proxy for homelab use. We need a runtime and proxy library
that provides:

- High-performance HTTP forwarding with low resource usage
- Mature TLS termination with SNI support and dynamic certificate selection
- Hot-reloadable proxy configuration (add/remove routes without restart)
- Health checking of backend services
- A strong ecosystem for DNS, ACME, and OpenTelemetry integration

Languages considered: Go, Rust, C#/.NET, Python, TypeScript/Node.js.

## Decision

We will use **.NET 10 LTS** (C# 14) with **YARP (Yet Another Reverse Proxy)** by Microsoft.

- .NET 10 is the current LTS release (November 2025, supported until November 2028).
- YARP is a production-grade reverse proxy library built on ASP.NET Core's Kestrel server.
  It is maintained by Microsoft and used internally in Azure services.
- YARP provides built-in support for dynamic route configuration, health checks, load balancing,
  and integrates natively with Kestrel's TLS pipeline.

## Consequences

**Positive:**
- YARP handles the heavy lifting of HTTP proxying, connection pooling, and header forwarding.
- Kestrel provides high-performance TLS termination with `ServerCertificateSelector` for
  dynamic certificate rotation.
- The .NET OTel SDK is mature and first-party supported.
- Single-binary deployment via `dotnet publish` or Docker image.
- Strong typing (C# 14) catches configuration errors at compile time.

**Negative:**
- .NET runtime has a larger memory footprint (~30-50 MB) compared to Go or Rust.
  Acceptable for a homelab single-box deployment.
- Certes (our ACME library) targets .NET Standard 2.0, not modern .NET — functional but
  not idiomatic. Acceptable given cert renewal is not performance-sensitive.
- Developer must have .NET SDK installed for local development.

**Risks:**
- YARP is maintained by Microsoft but is not part of the core ASP.NET framework.
  If abandoned, we would need to replace it with raw Kestrel middleware. Low risk given
  Microsoft's investment in YARP.
