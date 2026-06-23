# g0v0.Server.Realtime

This project is a part of g0v0 server v2.
It is the realtime server for g0v0 server series which provides realtime services like spectating, chatting,
multiplaying, online status and so on.

## Current Status

This project is under development. Some features are already implemented. You can see below for the current status of
each feature.

- [ ] Lazer
    - [x] Online status (MetadataHub)
    - [x] Spectating (SpectateHub)
    - [ ] Multiplay (MultiplayHub)
        - [ ] Standard Play
        - [ ] Ranked Play
- [ ] Chatting (NotificationServer)
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
  "UseLegacyDatabase": true
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
  "ReplayUploaderConcurrency": 4
}
```

| Key                         | Type | Default | Description                                           |
|-----------------------------|------|---------|-------------------------------------------------------|
| `SaveReplays`               | bool | `true`  | Whether to persist spectator replays (hot-reloadable) |
| `ReplayUploaderConcurrency` | int  | `4`     | Number of concurrent replay upload workers            |

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

## License

g0v0 server v2 series is licensed under the [MIT licence](https://opensource.org/licenses/MIT). Please
see [the licence file](LICENCE) for more information.
[tl;dr](https://tldrlegal.com/license/mit-license) you can do whatever you want as long as you **include the original
copyright and license notice in any copy of the software/source**.

"osu!" or "ppy" is a registered trademark of ppy Pty Ltd and is not affiliated with g0v0 server.
