# g0v0.Server.Realtime

This project is a part of g0v0 server v2. It is the realtime server for g0v0 server series which provides realtime
services like spectating, chatting, multiplaying, online status and so on.

## Current Status

This project is under development. Some features are already implemented. You can see below for the current status of
each feature.

- [ ] Lazer
    - [x] Online status (MetadataHub)
    - [x] Spectating (SpectatorHub)
    - [ ] Multiplay (MultiplayerHub)
        - [x] Standard Play
        - [ ] Ranked Play
    - [ ] RefereeHub
- [x] Chatting (NotificationServer)
    - [x] Channels, messages and silences (REST API)
    - [x] Real-time chat and notification events (`/notification-server` WebSocket)
    - [x] BanchoBot commands
    - [x] Private notifications (IPC + WebSocket)
- [ ] g0v0 Hub for other servers

## Try this project

**CRITICAL:** This project is under development. It is not recommended to use it in production environment.

### Clone this project and the commonlib

```bash
mkdir g0v0.Server
cd g0v0.Server
git clone https://github.com/GooGuTeam/g0v0.Server.Common.git g0v0.Server.Common
git clone https://github.com/GooGuTeam/g0v0.Server.Realtime.git g0v0.Server.Realtime
```

### Toggle g0v0-server v1 to enable V2 IPC support

[g0v0-server v1](https://github.com/GooGuTeam/g0v0-server) has supported the implemented features. You
can [modify the configuration](https://docs.g0v0.top/lazer/reference/configurations.html) to enable V2 IPC support:

```env
ENABLE_V2_IPC=true
```

### Configure the realtime server

The realtime server uses JSON configuration files located under `{ContentRoot}/config/`. Please create the following
files and fill in the values according to your environment from g0v0-server v1.

#### `config/general.json` — Database & Auth

```json
{
  "MySqlHost": "localhost",
  "MySqlPort": 3306,
  "MySqlUsername": "osu_api",
  "MySqlPassword": "password",
  "MySqlDatabase": "osu_api",
  "JwtSecretKey": "your_jwt_secret_here",
  "JwtAlgorithm": "HS256",
  "JwtAudience": "5",
  "JwtIssuer": null,
  "RedisHost": "localhost:6379",
  "UseLegacyDatabase": true,
  "FetcherClientId": 0,
  "FetcherClientSecret": "",
  "FetcherBeatmapRawCacheExpireHours": 24
}
```

Mapping from v1 `.env`:

| v1 `.env`        | v2 `general.json`   | Description                      |
|------------------|---------------------|----------------------------------|
| `MYSQL_HOST`     | `MySqlHost`         | MySQL server address             |
| `MYSQL_PORT`     | `MySqlPort`         | MySQL server port                |
| `MYSQL_DATABASE` | `MySqlDatabase`     | MySQL database name              |
| `MYSQL_USER`     | `MySqlUsername`     | MySQL user                       |
| `MYSQL_PASSWORD` | `MySqlPassword`     | MySQL password                   |
| `REDIS_URL`      | `RedisHost`         | Redis connection                 |
| `JWT_SECRET_KEY` | `JwtSecretKey`      | JWT signing key                  |
| `JWT_ALGORITHM`  | `JwtAlgorithm`      | JWT algorithm                    |
| `JWT_AUDIENCE`   | `JwtAudience`       | JWT audience                     |
| `JWT_ISSUER`     | `JwtIssuer`         | JWT issuer                       |
| —                | `UseLegacyDatabase` | `true` for MySQL (v1 compatible) |
| `FETCHER_CLIENT_ID`     | `FetcherClientId`                 | osu! API client ID used by the Fetcher service       |
| `FETCHER_CLIENT_SECRET` | `FetcherClientSecret`             | osu! API client secret used by the Fetcher service   |
| —                       | `FetcherBeatmapRawCacheExpireHours` | Raw beatmap cache duration in hours (default `24`) |

#### `config/game.json` — Game Features

```json
{
  "EnableRelax": true,
  "EnableAutopilot": true,
  "EnableAllBeatmapLeaderboard": true
}
```

| v1 `.env`                        | v2 `game.json`                | Description                         |
|----------------------------------|-------------------------------|-------------------------------------|
| `ENABLE_RX` / `ENABLE_OSU_RX`    | `EnableRelax`                 | Enable Relax mod statistics         |
| `ENABLE_AP` / `ENABLE_OSU_AP`    | `EnableAutopilot`             | Enable Autopilot mod statistics     |
| `ENABLE_ALL_BEATMAP_LEADERBOARD` | `EnableAllBeatmapLeaderboard` | Enable leaderboard for all beatmaps |

Properties marked with `[Reloadable]` support hot-reload at runtime.

#### `config/realtime.json` — Realtime-specific Settings

```json
{
  "SaveReplays": true,
  "ReplayUploaderConcurrency": 4,
  "MaxStartedScores": 2,
  "HostPlaylistLimit": 50,
  "GuestPlaylistLimit": 3,
  "MultiplayerDisconnectGracePeriod": "00:00:30",
  "TournamentRoomRetentionPeriod": "00:30:00",
  "Sentry": {
    "Dsn": "",
    "Environment": "",
    "TracesSampleRate": 0.1,
    "SendDefaultPii": false
  }
}
```

| Key                                | Type     | Default    | Description                                                                           |
|------------------------------------|----------|------------|---------------------------------------------------------------------------------------|
| `SaveReplays`                      | bool     | `true`     | Whether to persist spectator replays (hot-reloadable)                                 |
| `ReplayUploaderConcurrency`        | int      | `4`        | Number of concurrent replay upload workers (read once at startup; restart required)   |
| `MaxStartedScores`                 | int      | `2`        | Max concurrent started scores per spectator play session (hot-reloadable)             |
| `HostPlaylistLimit`                | int      | `50`       | Max playlist items a host or referee may enqueue (restart required)                   |
| `GuestPlaylistLimit`               | int      | `3`        | Max playlist items a guest may enqueue (restart required)                             |
| `MultiplayerDisconnectGracePeriod` | TimeSpan | `00:00:30` | How long a disconnected user is kept in their room before removal (hot-reloadable)    |
| `TournamentRoomRetentionPeriod`    | TimeSpan | `00:30:00` | How long an empty tournament room is retained before being destroyed (hot-reloadable) |
| `Sentry.Dsn`                       | string   | `""`       | Sentry DSN; Sentry is enabled automatically when this is non-empty (secret, git-ignored; restart required) |
| `Sentry.Environment`               | string   | `""`       | Sentry environment tag; empty falls back to `ASPNETCORE_ENVIRONMENT` (restart required) |
| `Sentry.TracesSampleRate`          | double   | `0.1`      | Performance-trace sampling rate 0.0–1.0 (restart required)                            |
| `Sentry.SendDefaultPii`            | bool     | `false`    | Whether default PII (user IDs, emails, IPs) is attached to events (restart required)  |

#### `config/storage.json` — Storage Backend

```json
{
  "Type": "Local",
  "Options": {
    "local_storage_path": "./storage"
  }
}
```

| v1 `.env`          | v2 `storage.json` | Description                              |
|--------------------|-------------------|------------------------------------------|
| `STORAGE_SERVICE`  | `Type`            | Storage type: `Local`, `S3`, or `R2`     |
| `STORAGE_SETTINGS` | `Options`         | Provider-specific settings (JSON object) |

### Run and connect to the server.

```bash
dotnet run --project g0v0.Server.Realtime/g0v0.Server.Realtime.csproj
```

Then you can run the server and connect to it with lazer. The url path is same as v1 server.

- Spectator Url: `/signalr/spectator`
- Metadata Url: `/signalr/metadata`
- Multiplayer Url: `/signalr/multiplayer`
- Notification Url: `/notification-server` (WebSocket)

## Docker

The Docker image is published to GitHub Packages
([ghcr.io/GooGuTeam/g0v0.Server.Realtime](https://github.com/GooGuTeam/g0v0.Server.Realtime/pkgs/container/g0v0.Server.Realtime))
on every push to `main` and version tags (`v*`) by the `.github/workflows/docker-image.yml` workflow.

### Building the image

The realtime project references the sibling repo [g0v0.Server.Common](https://github.com/GooGuTeam/g0v0.Server.Common)
via `ProjectReference`, so the image is built with a named build context for it:

```bash
# From the g0v0.Server.Realtime repo root
docker buildx build \
  --build-context common=/path/to/g0v0.Server.Common \
  -f g0v0.Server.Realtime/Dockerfile \
  -t ghcr.io/googuteam/g0v0.server.realtime:latest .
```

### Running the container

The image runs as a non-root user (`$APP_UID`) on port `8080`. Runtime data lives in three volumes that must be mounted:

| Volume          | Purpose                                                                                 |
|-----------------|-----------------------------------------------------------------------------------------|
| `/app/config`   | JSON configuration files (`general.json`, `game.json`, `realtime.json`, `storage.json`) |
| `/app/rulesets` | Custom osu! ruleset DLLs (loaded at startup; optional)                                  |
| `/app/storage`  | Local file storage (spectator replays etc.)                                             |

```bash
docker run --rm -p 8080:8080 \
  -v /host/path/config:/app/config:ro \
  -v /host/path/rulesets:/app/rulesets:ro \
  -v /host/path/storage:/app/storage \
  ghcr.io/googuteam/g0v0.server.realtime:latest
```

Config files are read from `{ContentRoot}/config/` — see [Configure the realtime server](#configure-the-realtime-server)
for the schema. The config, rulesets and storage directories are **not** baked into the image (and are excluded from the
build context by `.dockerignore`), so secrets such as DB passwords and JWT keys stay on the host.

## License

g0v0 server v2 series is licensed under the [MIT licence](https://opensource.org/licenses/MIT). Please
see [the licence file](LICENCE) for more information.
[tl;dr](https://tldrlegal.com/license/mit-license) you can do whatever you want as long as you **include the original
copyright and license notice in any copy of the software/source**.

"osu!" or "ppy" is a registered trademark of ppy Pty Ltd and is not affiliated with g0v0 server.
