# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

MobySync is a C# ASP.NET Core 8 background service that auto-updates Docker containers in home lab or production environments. It monitors labeled containers, pulls new images, replaces containers atomically with rollback support, and sends Discord notifications.

## Commands

```bash
# Build and run locally (requires .NET 8 SDK)
dotnet run

# Build for production (multi-stage Docker build)
docker build -t mobysync .

# Run with Docker Compose
docker compose up -d

# Test the trigger endpoint (requires running instance)
# See MobySync.http for request examples
curl -X POST http://localhost:5080/api/update/trigger -H "X-API-KEY: your-key"
```

There is no test suite. Use `MobySync.http` with a REST client for integration testing.

## Architecture

### Update Flow

The update cycle flows through three layers:

1. **`UpdateService`** (background service) — calculates daily schedule from `UPDATE_HOUR`/`UPDATE_MINUTE`/`TZ` env vars and calls `UpdateCoordinator.ExecuteScheduledUpdate()` at the right time
2. **`UpdateCoordinator`** — holds a `SemaphoreSlim(1,1)` so only one cycle runs at a time; also handles manual triggers via `TryStartManualUpdate()` (non-blocking, returns 409 if busy); collects `UpdateSummary` and fires notifications
3. **`DockerHelper`** — all Docker operations: discovers monitored containers, groups dependents via DFS topological sort, pulls images, and runs atomic replacements

### Container Discovery & Labels

Only containers with `com.mobysync.enable=true` are processed. Two additional labels control behavior:
- `com.mobysync.target-tag` — the image tag to track (e.g., `latest`, `stable`)
- `com.mobysync.depends-on` — comma-separated container names; determines update order via topological sort

### Atomic Replacement with Rollback

`DockerHelper.ReplaceContainers()` uses a stack-based transaction log (`ContainerCheckpoint`):
1. Rename old container to `{name}-backup`
2. Create and start new container with original name
3. Wait 10 seconds (stability check for crash detection)
4. Push checkpoint to stack; if any step fails, `AttemptRollback()` unwinds the stack in reverse order, restoring `-backup` containers

Image update detection compares **ImageID** (not tag), so a container tagged `latest` pointing to an already-current digest is skipped.

### Configuration Split

Settings come from two sources that must both be correct:
- `appsettings.json`: `ApiSection` (scheme, IP, port the server binds to) and log levels
- Environment variables: `API_KEY` (required, validated at startup), `UPDATE_HOUR`, `UPDATE_MINUTE`, `TZ`, `PRUNE_IMAGES`, `DISCORD_WEBHOOK_URL`

`API_KEY` absence causes immediate startup failure. `ApiSection` misconfiguration also aborts startup.

### Credentials

`CredentialsHelper` reads `/app/creds/config.json` (Docker's `config.json` format, mounted as a read-only volume). It decodes base64 credentials per registry and passes `AuthConfig` to Docker.DotNet pull calls. Falls back to anonymous pulls if the file is absent.

### API

Single endpoint: `POST /api/update/trigger` protected by `ApiKeyFilter` (checks `X-API-KEY` header). Returns `202 Accepted` or `409 Conflict`. Defined in `Extensions/RouteExtensions.cs`.

### Notifications

`INotificationHelper` abstraction; currently only `DiscordNotificationHelper` is implemented. Sends color-coded embeds: green for clean updates, red for rollbacks or pull failures.
