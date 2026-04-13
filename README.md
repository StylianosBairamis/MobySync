#  MobySync

MobySync is a lightweight Docker image auto-updater designed for home labs and production environments. 
It monitors your running containers, pulls new images, and safely replaces containers while respecting dependency chains and providing automatic rollbacks on failure.

---

##  Key Features

- **Automated Updates:** Schedule daily updates at specific times (e.g., 3:30 AM).
- **Dependency Management:** Define update orders using labels (e.g., ensure your database updates before your web app).
- **Safety First:** Automatic rollback to the previous container state if an update fails or the new container crashes.
- **Manual Triggers:** REST API endpoint to force an update cycle instantly.
- **Notifications:** Integrated Discord webhook support for success and failure alerts.
- **Housekeeping:** Optional automatic image pruning to save disk space.

---

##  Configuration

### Environment Variables

| Variable | Description | Default |
| :--- | :--- | :--- |
| `API_KEY` | **(Required)** Secret key for API authentication | - |
| `UPDATE_HOUR` | Hour of the day to run updates (0-23) | `23` |
| `UPDATE_MINUTE` | Minute of the hour to run updates (0-59) | `30` |
| `TZ` | Timezone for the update schedule (e.g., `Europe/London`) | `UTC` |
| `PRUNE_IMAGES` | Set to `true` to remove old images after updates | `false` |
| `DISCORD_WEBHOOK_URL` | URL for Discord notifications | - |

### Container Labels

MobySync only monitors containers that you explicitly authorize via labels:

| Label | Description | Example |
| :--- | :--- | :--- |
| `com.mobysync.enable` | Enable monitoring for this container | `true` |
| `com.mobysync.target-tag` | The specific tag to track | `latest` or `stable` |
| `com.mobysync.depends-on` | Comma-separated list of container names | `db-container,redis-cache` |

---

##  Deployment

```yaml
services:
  mobysync:
    image: steliosbairam/mobysync:latest
    container_name: mobysync
    restart: unless-stopped
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock
      # - ./config:/app/config # For custom credentials mapping
    environment:
      - API_KEY=your_secure_random_key
      - UPDATE_HOUR=03
      - UPDATE_MINUTE=00
      - TZ=America/New_York
      - PRUNE_IMAGES=true
      - DISCORD_WEBHOOK_URL=https://discord.com/api/webhooks/...
    ports:
      - "5080:5080"
```

---

## API Reference

### Trigger Manual Update
Instantly starts a check for all monitored containers.

- **URL:** `/api/update/trigger`
- **Method:** `POST`
- **Header:** `X-API-KEY: your_secure_random_key`

**Responses:**
- `202 Accepted`: Update process started in the background.
- `409 Conflict`: An update process is already running.
- `401 Unauthorized`: Missing or invalid API key.

---


## License

Distributed under the MIT License. See `LICENSE` for more information.
