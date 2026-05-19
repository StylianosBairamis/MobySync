# MobySync

MobySync is a lightweight Docker image auto-updater designed for home labs and production environments.
It monitors all running containers, pulls new images, and safely replaces them — respecting dependency chains, rolling back on failure, and notifying you on every cycle.

---

## Key Features

- **Opt-out monitoring** — all running containers are watched by default; exclude what you don't want touched
- **Scheduled updates** — runs once a day at a time and timezone you choose
- **Pinned version detection** — containers on tags like `16.3` or `v1.2.3` are automatically skipped
- **Locally built image detection** — images with no registry digest are skipped without aborting other updates
- **Dependency ordering** — define update order between containers via a label
- **Atomic replacement with rollback** — if a new container crashes within 10 seconds, MobySync restores the backup automatically
- **Private registry support** — reads Docker's `config.json` for credentials; retries anonymously if credentials fail on a public image
- **Notifications** — Discord embed webhook and/or a generic JSON webhook (works with n8n, Zapier, custom endpoints)
- **Manual trigger** — REST API to kick off an update cycle on demand
- **Image pruning** — optional cleanup of dangling images after each cycle

---

## How Monitoring Works

MobySync monitors **all running containers** except:

1. Itself (detected automatically — no configuration needed)
2. Containers listed in `EXCLUDED_CONTAINERS`
3. Containers using a **locally built image** (no registry digest — skipped with a note in the summary)
4. Containers on a **pinned version tag** like `16.3`, `18.3.1`, or `v2.1.0` (skipped automatically)

Floating tags like `latest`, `stable`, `edge`, and `nightly` are updated normally.

---

## Configuration

### Environment Variables

| Variable | Required | Description | Default |
| :--- | :---: | :--- | :--- |
| `API_KEY` | Yes | Secret key for the manual-trigger API | — |
| `UPDATE_HOUR` | | Hour of day to run updates (0–23) | `23` |
| `UPDATE_MINUTE` | | Minute of hour to run updates (0–59) | `30` |
| `TZ` | | IANA timezone for the schedule (e.g. `Europe/London`) | `UTC` |
| `EXCLUDED_CONTAINERS` | | Comma-separated container names to never touch | — |
| `IMAGE_TAG_OVERRIDES` | | Force a specific tag for an image or Compose service (see below) | — |
| `PRUNE_IMAGES` | | Set to `true` to remove dangling images after each cycle | `false` |
| `DISCORD_WEBHOOK_URL` | | Discord embed webhook URL | — |
| `WEBHOOK_URL` | | Generic JSON webhook URL | — |

### Excluding Containers

```yaml
EXCLUDED_CONTAINERS: "db,redis,my-custom-app"
```

Any container whose name exactly matches one of these values (case-insensitive) will never be touched.

### Tag Resolution Order

For each container, MobySync resolves which tag to track using this priority:

1. **`com.mobysync.target-tag` label** on the container
2. **Stack override** in `IMAGE_TAG_OVERRIDES` (`project:service:tag` format)
3. **Image override** in `IMAGE_TAG_OVERRIDES` (`image:tag` format)
4. **Auto-detected** from the running container's image string (e.g. `neosmemo/memos:stable` → `stable`)
5. **`latest`** as a last resort

In most cases you don't need to configure anything — MobySync reads the tag the container is already running.

### IMAGE_TAG_OVERRIDES

Use this only when you want to **change** which tag a container tracks (e.g. migrate from `stable` to `latest`). Two formats are supported:

```yaml
# project:service:tag  — targets a specific Compose service
# image:tag            — targets any container using that base image
IMAGE_TAG_OVERRIDES: "mystack:memos:latest,neosmemo/memos:latest"
```

Per-container labels always take priority over overrides.

### Container Labels

| Label | Description | Example |
| :--- | :--- | :--- |
| `com.mobysync.target-tag` | Pin this container to a specific tag | `stable` |
| `com.mobysync.depends-on` | Comma-separated containers that must update first | `db,redis` |

**Example — Compose service that must update after its database:**

```yaml
services:
  db:
    image: postgres:16
    labels:
      com.mobysync.target-tag: "16"

  app:
    image: myapp/server:latest
    labels:
      com.mobysync.depends-on: "db"
```

---

## What Gets Skipped (and Why)

MobySync adds skipped containers to the notification summary so you always have the full picture.

| Scenario | Reason shown in notification |
| :--- | :--- |
| Container uses a locally built image | `Locally built image — cannot check for updates` |
| Container tag is a pinned version (`16.3`, `v1.2.3`) | `Pinned version tag '16.3' — skipping auto-update` |
| Listed in `EXCLUDED_CONTAINERS` | Never appears — excluded before the cycle starts |

---

## Private Registries & Credentials

Mount your Docker credentials file to give MobySync access to private registries or authenticated Docker Hub pulls:

```yaml
volumes:
  - ~/.docker/config.json:/app/creds/config.json:ro
```

If this volume is absent, MobySync pulls anonymously. If credentials are present but a pull still fails with an auth error (e.g. stale Docker Hub token on a public image), MobySync automatically retries anonymously.

Supported credential key formats in `config.json`:
- `https://index.docker.io/v1/` (Docker Desktop default)
- `registry-1.docker.io` (Podman, some CI toolchains)
- `docker.io` (older Docker CLI)
- Any custom registry hostname (e.g. `ghcr.io`, `registry.mycompany.com`)

---

## Notifications

MobySync sends a notification at three points during its lifecycle:

| Event | Description |
| :--- | :--- |
| **Startup** | Sent once on boot to confirm the webhook is working |
| **Cycle started** | Sent at the beginning of each update cycle |
| **Cycle summary** | Sent at the end with full results — always fires, even when nothing changed |

### Discord

Set `DISCORD_WEBHOOK_URL` to receive color-coded embeds:
- **Blue** — cycle starting or no changes found
- **Green** — all updates succeeded
- **Red** — one or more rollbacks or failed pulls

Example summary embed when updates are found:

```
Update Cycle Summary
✅ Successful Updates
• homepage: latest
• portainer: latest

⏭️ Skipped
• memos (neosmemo/memos): Pinned version tag '0.24.1' — skipping auto-update
• myapp (myapp/server): Locally built image — cannot check for updates

Total duration: 01:32
```

Example when nothing changed:

```
Update Cycle Complete
All containers are up to date.
`homepage`, `portainer`, `uptime-kuma`

⏭️ Skipped
• memos (neosmemo/memos): Pinned version tag '0.24.1' — skipping auto-update
```

### Generic JSON Webhook

Set `WEBHOOK_URL` to receive structured JSON payloads. Works with n8n, Zapier, Make, or any custom endpoint.

**Startup payload:**
```json
{
  "event": "startup",
  "timestamp": "2026-05-19T20:00:00Z",
  "message": "MobySync started. Webhook is configured and working."
}
```

**Cycle started payload:**
```json
{
  "event": "update_started",
  "timestamp": "2026-05-19T23:30:00Z",
  "message": "Update cycle starting. Checking all containers for new images."
}
```

**Cycle summary payload:**
```json
{
  "event": "update_cycle",
  "timestamp": "2026-05-19T23:31:32Z",
  "durationSeconds": 92,
  "successes": [
    { "container": "homepage", "image": "ghcr.io/gethomepage/homepage", "oldTag": "latest", "newTag": "latest" }
  ],
  "rollbacks": [],
  "failedPulls": [],
  "skipped": [
    { "container": "memos", "image": "neosmemo/memos", "reason": "Pinned version tag '0.24.1' — skipping auto-update" }
  ],
  "upToDate": ["portainer", "uptime-kuma"]
}
```

---

## Deployment

```yaml
services:
  mobysync:
    image: steliosbairam/mobysync:latest
    container_name: mobysync
    restart: unless-stopped
    ports:
      - "5080:5080"
    environment:
      TZ: "Europe/Athens"
      API_KEY: "super-secret-key"
      # Comma-separated container names to never touch
      EXCLUDED_CONTAINERS: "db,redis"
      # Discord embed webhook
      DISCORD_WEBHOOK_URL: ""
      # Generic JSON webhook (n8n, Zapier, custom endpoints)
      WEBHOOK_URL: ""
      # Set to "true" to remove dangling images after each cycle
      PRUNE_IMAGES: "false"
      # Force a tag override when auto-detection isn't enough:
      #   project:service:tag  or  image:tag
      # IMAGE_TAG_OVERRIDES: "mystack:memos:latest,neosmemo/memos:latest"
      UPDATE_HOUR: 23
      UPDATE_MINUTE: 30
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock
      # Optional: mount Docker credentials for private registries.
      # Remove if you only pull from public images.
      - ~/.docker/config.json:/app/creds/config.json:ro
```

---

## API Reference

### Trigger Manual Update

Immediately starts a full update cycle in the background.

- **URL:** `POST /api/update/trigger`
- **Header:** `X-API-KEY: <your API_KEY>`

**Responses:**

| Status | Meaning |
| :--- | :--- |
| `202 Accepted` | Cycle started |
| `409 Conflict` | A cycle is already running |
| `401 Unauthorized` | Missing or invalid API key |

**Example:**
```bash
curl -X POST http://localhost:5080/api/update/trigger \
  -H "X-API-KEY: super-secret-key"
```

---

## License

Distributed under the MIT License. See `LICENSE` for more information.
