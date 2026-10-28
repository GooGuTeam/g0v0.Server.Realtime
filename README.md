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
        - [x] Tournament management commands (`!mp`)
        - [ ] Ranked Play
    - [ ] RefereeHub
- [x] Chatting (NotificationServer)
    - [x] Channels, messages and silences (REST API)
    - [x] Real-time chat and notification events (`/notification-server` WebSocket)
    - [x] BanchoBot commands
    - [x] Private notifications (IPC + WebSocket)
- [ ] g0v0 Hub for other servers

Multiplayer room creation, loading and metadata queries use `source = Realtime`; Stable rooms are
excluded. Apply Common's `AddRoomSource` PostgreSQL migration or
the `84cceb652fe5` room-source Alembic migration in `osu_lazer_api` before deploying this version. Existing room records
default to `Realtime`.

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
  "UseLegacyDatabase": true,
  "MySqlHost": "localhost",
  "MySqlPort": "3306",
  "MySqlUsername": "osu_api",
  "MySqlPassword": "password",
  "MySqlDatabase": "osu_api",
  "PostgresqlConnectionString": "Host=localhost;Port=5432;Username=g0v0;Password=password;Database=g0v0",
  "RedisHost": "localhost:6379",
  "JwtSecretKey": "your_jwt_secret_here",
  "JwtAlgorithm": "HS256",
  "JwtAudience": "5",
  "JwtIssuer": null,
  "OsuClientId": 5,
  "OsuClientSecret": "FGc9GAtyHzeQDshWP5Ah7dega8hJACAJpQtw6OXk",
  "OsuWebClientId": 6,
  "OsuWebClientSecret": "your_osu_web_client_secret_here",
  "ServerUrl": "http://localhost:8000",
  "FetcherClientId": 0,
  "FetcherClientSecret": "",
  "FetcherBeatmapRawCacheExpireHours": 24
}
```

Mapping from v1 `.env`:

| v1 `.env`                    | v2 `general.json`                   | Description                                                                     |
|------------------------------|-------------------------------------|---------------------------------------------------------------------------------|
| `MYSQL_HOST`                 | `MySqlHost`                         | MySQL server address                                                            |
| `MYSQL_PORT`                 | `MySqlPort`                         | MySQL server port (kept as a string)                                            |
| `MYSQL_DATABASE`             | `MySqlDatabase`                     | MySQL database name                                                             |
| `MYSQL_USER`                 | `MySqlUsername`                     | MySQL user                                                                      |
| `MYSQL_PASSWORD`             | `MySqlPassword`                     | MySQL password                                                                  |
| —                            | `PostgresqlConnectionString`        | PostgreSQL connection string (v2 backend, used when `UseLegacyDatabase: false`) |
| `REDIS_URL`                  | `RedisHost`                         | Redis connection                                                                |
| `JWT_SECRET_KEY`             | `JwtSecretKey`                      | JWT signing key                                                                 |
| `JWT_ALGORITHM`              | `JwtAlgorithm`                      | JWT algorithm                                                                   |
| `JWT_AUDIENCE`               | `JwtAudience`                       | JWT audience                                                                    |
| `JWT_ISSUER`                 | `JwtIssuer`                         | JWT issuer                                                                      |
| `OSU_CLIENT_ID`              | `OsuClientId`                       | osu! (lazer) OAuth client ID                                                    |
| `OSU_CLIENT_SECRET`          | `OsuClientSecret`                   | osu! (lazer) OAuth client secret                                                |
| `OSU_WEB_CLIENT_ID`          | `OsuWebClientId`                    | osu! web OAuth client ID                                                        |
| `OSU_WEB_CLIENT_SECRET`      | `OsuWebClientSecret`                | osu! web OAuth client secret                                                    |
| `SERVER_URL`                 | `ServerUrl`                         | Public server URL                                                               |
| —                            | `UseLegacyDatabase`                 | `true` for MySQL (v1 compatible), `false` for PostgreSQL (v2)                   |
| `FETCHER_CLIENT_ID`          | `FetcherClientId`                   | osu! API client ID used by the Fetcher service                                  |
| `FETCHER_CLIENT_SECRET`      | `FetcherClientSecret`               | osu! API client secret used by the Fetcher service                              |
| `BEATMAP_CACHE_EXPIRE_HOURS` | `FetcherBeatmapRawCacheExpireHours` | Raw beatmap cache duration in hours (default `24`)                              |

All configurations are restart required when changed.

#### `config/game.json` — Game Features

```json
{
  "EnableRelax": false,
  "EnableAutopilot": false,
  "EnableAllBeatmapLeaderboard": false
}
```

| v1 `.env`                        | v2 `game.json`                | Default | Description                         |
|----------------------------------|-------------------------------|---------|-------------------------------------|
| `ENABLE_RX` / `ENABLE_OSU_RX`    | `EnableRelax`                 | `false` | Enable Relax mod statistics         |
| `ENABLE_AP` / `ENABLE_OSU_AP`    | `EnableAutopilot`             | `false` | Enable Autopilot mod statistics     |
| `ENABLE_ALL_BEATMAP_LEADERBOARD` | `EnableAllBeatmapLeaderboard` | `false` | Enable leaderboard for all beatmaps |

All configurations are hot-reloadable at runtime.

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

| Key                                | Type     | Default    | Description                                                                                                |
|------------------------------------|----------|------------|------------------------------------------------------------------------------------------------------------|
| `SaveReplays`                      | bool     | `true`     | Whether to persist spectator replays (hot-reloadable)                                                      |
| `ReplayUploaderConcurrency`        | int      | `4`        | Number of concurrent replay upload workers (read once at startup; restart required)                        |
| `MaxStartedScores`                 | int      | `2`        | Max concurrent started scores per spectator play session (hot-reloadable)                                  |
| `HostPlaylistLimit`                | int      | `50`       | Max playlist items a host or referee may enqueue (restart required)                                        |
| `GuestPlaylistLimit`               | int      | `3`        | Max playlist items a guest may enqueue (restart required)                                                  |
| `MultiplayerDisconnectGracePeriod` | TimeSpan | `00:00:30` | How long a disconnected user is kept in their room before removal (hot-reloadable)                         |
| `TournamentRoomRetentionPeriod`    | TimeSpan | `00:30:00` | How long an empty tournament room is retained before being destroyed (hot-reloadable)                      |
| `MaxActiveTournamentRooms`         | int      | `4`        | Max number of tournament rooms a single user may own through `!mp make` (hot-reloadable)                    |
| `Sentry.Dsn`                       | string   | `""`       | Sentry DSN; Sentry is enabled automatically when this is non-empty (secret, git-ignored; restart required) |
| `Sentry.Environment`               | string   | `""`       | Sentry environment tag; empty falls back to `ASPNETCORE_ENVIRONMENT` (restart required)                    |
| `Sentry.TracesSampleRate`          | double   | `0.1`      | Performance-trace sampling rate 0.0–1.0 (restart required)                                                 |
| `Sentry.SendDefaultPii`            | bool     | `false`    | Whether default PII (user IDs, emails, IPs) is attached to events (restart required)                       |

These realtime settings are v2-only and have no v1 `.env` equivalent.

#### `config/chat.json` — Chat Limits

This file is **optional**. When `config/chat.json` is absent, every value falls back to the upstream
([osu-web](https://github.com/ppy/osu-web)) default
listed below. All configurations are hot-reloadable at runtime.

```json
{
  "ChannelLimit": 10000,
  "MessageLengthLimit": 450,
  "AnnounceMessageLengthLimit": 1024,
  "PublicBacklogLimitHours": 24,
  "ActivityTimeoutSeconds": 60,
  "RecentMessageLimit": 50,
  "PublicMessageLimit": 1,
  "PublicMessageWindowSeconds": 1,
  "PrivateMessageLimit": 1,
  "PrivateMessageWindowSeconds": 1
}
```

| Key                           | Type | Default | Description                                                                              |
|-------------------------------|------|---------|------------------------------------------------------------------------------------------|
| `ChannelLimit`                | int  | `10000` | Maximum number of channels listed for a single user                                      |
| `MessageLengthLimit`          | int  | `450`   | Maximum length of a chat message                                                         |
| `AnnounceMessageLengthLimit`  | int  | `1024`  | Maximum length of an announcement message                                                |
| `PublicBacklogLimitHours`     | int  | `24`    | How many hours of history are exposed for public channels                                |
| `ActivityTimeoutSeconds`      | int  | `60`    | How long a user stays active in a public channel after acknowledging it (hot-reloadable) |
| `RecentMessageLimit`          | int  | `50`    | Number of recent messages attached to a channel                                          |
| `PublicMessageLimit`          | int  | `1`     | Number of messages a user may post in a public channel per window                        |
| `PublicMessageWindowSeconds`  | int  | `1`     | Public message rate-limit window, in seconds                                             |
| `PrivateMessageLimit`         | int  | `1`     | Number of messages a user may post in a private channel per window                       |
| `PrivateMessageWindowSeconds` | int  | `1`     | Private message rate-limit window, in seconds                                            |

These chat limits are v2-only and have no v1 `.env` equivalent.

#### `config/faq.json` — FAQ Entries

This file is **optional**. When `config/faq.json` is absent, `!faq` reports that no entries are available
instead of failing. Changing the file requires a restart, as no reload trigger calls
`ConfigManager.Reload<FaqConfig>()` yet.

```json
{
  "DefaultLanguage": "en",
  "Entries": [
    {
      "Id": "contribution",
      "Translations": {
        "en": "You can also make a contribution to g0v0! if you wish. See our contribution guide for details.",
        "zh": "你也可以为 g0v0! 出一份力，只要你愿意！想了解更多，不妨看看我们的贡献指南。"
      }
    }
  ]
}
```

| Key                       | Type   | Default | Description                                                                                     |
|---------------------------|--------|---------|-------------------------------------------------------------------------------------------------|
| `DefaultLanguage`         | string | `en`    | Language returned when an entry has no translation for the requested one                        |
| `Entries[].Id`            | string | —       | Identifier used by `!faq <entry>`; matched case-insensitively, cannot contain spaces (`list` is reserved) |
| `Entries[].Translations`  | object | —       | Answer text per language code; keys are matched by their primary subtag, so `zh-CN` answers `zh:` |

Entries without an identifier or without a single non-blank translation are skipped: they are neither
listed by `!faq list` nor resolvable. Duplicate identifiers keep the first occurrence. Answers longer than
`MessageLengthLimit` are delivered as several messages, so a chat client never truncates them.

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

The `Options` object matches the v1 `STORAGE_SETTINGS` for each provider:

```jsonc
// Local (default for development)
{ "Type": "Local", "Options": { "local_storage_path": "./storage" } }

// Cloudflare R2 (recommended for production)
{
  "Type": "R2",
  "Options": {
    "r2_account_id": "your_cloudflare_account_id",
    "r2_access_key_id": "your_r2_access_key_id",
    "r2_secret_access_key": "your_r2_secret_access_key",
    "r2_bucket_name": "your_bucket_name",
    "r2_public_url_base": "https://your-custom-domain.com"
  }
}

// AWS S3
{
  "Type": "S3",
  "Options": {
    "s3_access_key_id": "your_aws_access_key_id",
    "s3_secret_access_key": "your_aws_secret_access_key",
    "s3_bucket_name": "your_s3_bucket_name",
    "s3_region_name": "us-east-1",
    "s3_public_url_base": "https://your-custom-domain.com"
  }
}
```

### Tournament management commands (`!mp`)

Because the rooms live in this server, the osu!wiki
[tournament management commands](https://osu.ppy.sh/wiki/osu%21_tournament_client/osu%21tourney/Tournament_management_commands)
are handled here instead of being relayed to another process. BanchoBot answers in the room channel, and commands are
authorised like the equivalent hub calls: the room host or a referee may use them, while `addref` / `removeref` are
host-only.

| Group | Commands |
| ----- | -------- |
| Room | `make <name>`, `close`, `settings`, `name <title>`, `password [password]`, `size <size>`, `set <0\|2>` |
| Players | `host`, `invite`, `kick`, `ban`, `addref`, `removeref`, `listrefs`, `move <user> <slot>`, `team <user> <red\|blue>` |
| Beatmap | `map <beatmap_id> [ruleset_id]`, `mods [acronym ...]` |
| Match | `start [seconds]`, `abort`, `timer [seconds]`, `aborttimer`, `lock`, `unlock` |

`makeprivate` and `clearhost` are rejected, as they are not modelled by this server. `!mp help` lists everything.
Rooms created with `!mp make` start empty: join the room from the multiplayer lobby before using `map` / `mods`, which
edit the room's playlist. Replies longer than `MessageLengthLimit` (`config/chat.json`) are delivered as several
messages instead of being truncated.

### FAQ command (`!faq`)

BanchoBot answers the short tips stored in `config/faq.json` (see the configuration section above), so the
answers can be changed without rebuilding the server.

| Input                                              | Reply                                                                  |
|----------------------------------------------------|------------------------------------------------------------------------|
| `!faq`                                             | `Usage: !faq [lang:]<entry>. Try !faq list to see all entries.`        |
| `!faq list`                                        | `Available entries: <id>, <id>.` in configuration order                |
| `!faq <entry>`                                     | The entry text in `DefaultLanguage`                                    |
| `!faq <lang>:<entry>`                              | The entry text in `<lang>`, a case-insensitive two letter code         |
| `!faq <lang>:<entry>` when that language is missing| The `DefaultLanguage` text, without any marker                         |
| An unknown entry                                   | `Entry not found!`                                                     |
| `!faq list` without any usable entry               | `No FAQ entries available.`                                            |
| An unreadable `config/faq.json`                    | `The FAQ is currently unavailable. Please try again later.`            |

Language codes are matched by their primary subtag, so a `zh-CN` translation answers both `zh:` and `zh-CN:`.
When the entry has no `DefaultLanguage` version either, any non-blank translation is returned, so every entry
shown by `!faq list` can be resolved. `list` is reserved and cannot be used as an identifier. Replies in
public channels go through the BanchoBot PM channel, like every other bot command; replies in room channels
stay in the room.

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

| Volume          | Purpose                                                                                                                       |
|-----------------|-------------------------------------------------------------------------------------------------------------------------------|
| `/app/config`   | JSON configuration files (`general.json`, `game.json`, `realtime.json`, `chat.json`, `storage.json`; `chat.json` is optional) |
| `/app/rulesets` | Custom osu! ruleset DLLs (loaded at startup; optional)                                                                        |
| `/app/storage`  | Local file storage (spectator replays etc.)                                                                                   |

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
