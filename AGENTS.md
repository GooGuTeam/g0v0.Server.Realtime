# AGENTS.md

ASP.NET Core 10 SignalR realtime server for g0v0 v2 (spectating, online status, multiplay). The solution has two projects: `g0v0.Server.Realtime/` (web host) and `g0v0.Server.Realtime.Tests/` (NUnit 4).

## Sibling-repo dependency (read first)

`g0v0.Server.Realtime/g0v0.Server.Realtime.csproj` has a `ProjectReference` to `..\..\g0v0.Server.Common\g0v0.Server.Common\g0v0.Server.Common.csproj`. Nothing in this repo restores or builds without `GooGuTeam/g0v0.Server.Common` checked out as a sibling directory:

```
<parent>/
  g0v0.Server.Common/      # https://github.com/GooGuTeam/g0v0.Server.Common
  g0v0.Server.Realtime/    # this repo
```

If `dotnet restore`/`build` fails with a missing project path, this is almost always the cause. CI mirrors this layout (`.github/workflows/ci.yml` checks both repos out side-by-side).

`Common` carries the EF Core repositories, configuration system, `IPathProvider`, IPC client, storage abstraction, and the `osu.Game.*` package references (`g0v0.osu.Game`, `ppy.osu.Game.Rulesets.*`). When something looks unimplemented here, check `Common` first — the `AGENTS.md` in that repo explains its conventions (config files via Newtonsoft, snake_case file names, repository reflection wiring, dual MySQL/PostgreSQL backends).

## Commands

Run from the repo root:

- Build: `dotnet build`
- Test all: `dotnet test`
- Single fixture: `dotnet test --filter "FullyQualifiedName~SpectatorHubTests"`
- Run server: `dotnet run --project g0v0.Server.Realtime/g0v0.Server.Realtime.csproj`
- Format check (CI gate): `dotnet format --verify-no-changes --exclude ../g0v0.Server.Common`
- Apply formatting: `dotnet format --exclude ../g0v0.Server.Common`
- Install InspectCode: `dotnet tool install --global JetBrains.ReSharper.GlobalTools --version 2026.2.1`
- InspectCode: `dotnet build --configuration Debug` first, then `jb inspectcode g0v0.Server.Realtime.sln --no-build --no-updates --severity=WARNING --swea --output=artifacts/inspectcode.sarif`
- Strict CI-equivalent build: `CI=true dotnet build` (warnings become errors, see below)

CI (`.github/workflows/ci.yml`) is two jobs:

- `code-quality`: `dotnet format --verify-no-changes`, a **Debug** build with warnings as errors, then warning-level
  JetBrains InspectCode. The SARIF report goes to GitHub code scanning (`Security` → `Code scanning`, category
  `resharper-inspectcode`), and any remaining warning/error fails the job.
- `test`: `dotnet test --configuration Release`.

Everything is scoped to **this repository**, because `g0v0.Server.Common` is a sibling checkout that must never fail
this repo's CI: `dotnet format --exclude ../g0v0.Server.Common` (`--include` is *not* usable — it silently turns
`--verify-no-changes` into a no-op), `TreatWarningsAsErrors` set from `Directory.Build.props` on `$(CI)` only (Common
has its own props file and never inherits it), and a `jq` pass that drops any InspectCode result whose path escapes
the solution directory.

Two details that are easy to break: the InspectCode step builds **Debug** because that is the configuration
`--no-build` evaluates, and the SARIF upload needs `checkout_path` because the checkout steps put the repository one
level down (`path: g0v0.Server.Realtime`) — without it every result is dropped as out-of-repository.

`global.json` pins SDK `10.0.0` (`rollForward: latestMinor`). `Directory.Build.props` injects StyleCop.Analyzers, Meziantou.Analyzer, Roslynator, `EnforceCodeStyleInBuild`, and `GenerateDocumentationFile` into every project — do not duplicate those in `.csproj`.

## Runtime configuration (not in source control)

`.gitignore` excludes `config/` and `storage/`. The server reads JSON files from `{ContentRoot}/config/`:

- `general.json` — DB + JWT + Redis (`UseLegacyDatabase: true` → MySQL v1 schema, `false` → PostgreSQL v2). Loaded from `Common`.
- `game.json` — feature flags.
- `realtime.json` — `SaveReplays` (`[Reloadable]`), `ReplayUploaderConcurrency` (read once at startup; see `Services/ScoreUploader.cs`), `MaxStartedScores` (`[Reloadable]`; read per begin-play call in `PlayerBase.BeingPlaying`).
- `storage.json` — `Local`/`S3`/`R2`.

Config classes use **Newtonsoft.Json**; default file name is the snake_case of the class name (override with `[ConfigurationFile("...")]`). Only properties annotated `[Reloadable]` are refreshed by `Reload<T>()`. See README for full schema and v1→v2 env-var mapping.

## Code style (non-default)

Enforced by `.editorconfig`, `.globalconfig`, `stylecop.json`, and the shared `Directory.Build.props`:

- File header on every `.cs`: `// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.` followed by a blank line.
- `using` directives **outside** the namespace, `System.*` first, no blank lines between groups; file-scoped namespaces; LF; 4 spaces; no trailing newline.
- SA1124 (regions) and SA1309 (`_underscore` fields) are intentionally disabled. `PlayerFacade`/`IPlayerFacade` even use **public `_underscore` properties** as a dependency-injection facade — preserve this pattern; don't rename them.
- XML doc comments are required on public/exposed members (`documentExposedElements: true`); the test project sets `<NoWarn>SA0001;SA1600;SA1602;CS1591;CS0618;MA0048</NoWarn>` so tests deliberately skip them.
- `IDE0005` (unused usings) and `IDE0055` (formatting) are warnings, promoted to errors in CI (`CI=true`, mapped to
  `TreatWarningsAsErrors` in `Directory.Build.props`).

## Architecture

Entrypoint: `g0v0.Server.Realtime/Program.cs`. Wiring order matters — `ConfigurationManager` (constructed from `IHostEnvironment.ContentRootPath` via `WebPathProvider`) is registered first so `AddRepositories`, `AddStorage`, `AddRedis`, etc. (extension methods from `Common`) can resolve it.

SignalR specifics:
- MessagePack protocol only. JSON is explicitly removed in `Program.cs`'s `ConfigureClientHubOptions` because some union types don't round-trip through JSON.
- `SignalRUnionWorkaroundResolver.OPTIONS` (from `Common`) is required for derived match-state messages.
- `IUserIdProvider` is replaced by `JwtUserIdProvider` (`JwtUserIdProvider.cs`). It reads only Common's database-verified
  `OAuthClaimTypes.UserId` (`osu_user_id`) claim; `HubCallerContextExtensions.GetUserId()` reads the resulting
  `UserIdentifier`. JWT `sub`/`NameIdentifier` may identify an OAuth client and must not be used as user IDs.
  `AddRealtimeAuthentication()` validates header tokens and `access_token` query tokens under `/signalr` and
  `/notification-server` through Common's database-backed handler. HTTP controllers and the notification WebSocket
  use the same verified user claim.
- Endpoints: `/signalr/spectator` (`SpectatorHub`), `/signalr/metadata` (`MetadataHub`) and `/signalr/multiplayer`
  (`MultiplayerHub`). All inherit `LazerRealtimeHub<TClient>`.
- `SpectatorHub` exposes both the legacy V1 API (`BeginPlaySession`, `SendFrameData`, `EndPlaySession` — marked `[Obsolete]`, removal date 20270102) and the V2 API (`BeginPlaySessionV2`, `SendFrameDataV2`, `EndPlaySessionV2`) with explicit per-score tokens. Both routes share one player-side implementation (`IPlayer.BeingPlaying`/`SendFrames`/`FinishPlaying`): `PlayerState.ScoreTokens` tracks up to the configured `RealtimeConfig.MaxStartedScores` concurrent started scores (oldest is evicted and processed on overflow), the ambient `ScoreToken` mirrors the most recently started score, and only ending the latest score (or with a null token) broadcasts `UserFinishedPlaying`. V1 hub methods pass the ambient token; V2 methods pass the explicit one.

### Multiplayer (rooms & match flow)

- `MultiplayerHub` implements `IMultiplayerServer` (lounge + room server). Every command validates the caller against
  server-side room state, then runs inside the room's `IRoom.RoomLock` (`SemaphoreSlim`) so concurrent hub calls are
  serialised. `PlayerState.CurrentRoom` tracks which room a player is in; joining/leaving updates it and the room's
  participant table through `IRoomRepository`.
- Room model: `RoomBase` (abstract) holds the state machine, persistence and event broadcasting; `StandardRoom` adds
  the current-playlist-item resolution and capacity checks; `HeadToHeadRoom` and `TeamVersusRoom` implement the two
  supported match types. `MatchType.Playlists`, `Matchmaking` and `RankedPlay` are explicitly rejected at creation and
  settings change.
- State machine (server-authoritative): `Idle -> Ready`, server sets `WaitingForLoad` on start, clients send
  `Loaded -> ReadyForGameplay`, server sets `Playing`, clients send `FinishedPlay`, server sets `Results` then returns
  the room to `Open`. Client attempts to set server-managed states (`WaitingForLoad`, `Playing`, `Results`) throw
  `InvalidStateChangeException`; late/racing `Idle`/`Loaded`/`ReadyForGameplay` messages are silently ignored.
- `IsReadyForGameplay()` (in `Extensions/MultiplayerUserStateExtensions.cs`) requires
  `BeatmapAvailability.State == DownloadState.LocallyAvailable` and `Ready`/`Idle` — clients must call
  `ChangeBeatmapAvailability` before `StartMatch` succeeds.
- `MultiplayerEventDispatcher` is the sole broadcaster. Room-wide events go to the SignalR group
  `multiplayer:room:{roomId}` (joined on connect, left on leave/kick); per-user events (`LoadRequested`,
  `GameplayStarted`, `GameplayAborted`, `ResultsReady`, `Invited`, kicked unicast) go to `Clients.User(userId)`.
  `Program.ConfigureServices` registers `MultiplayerEventDispatcher`, `IRoomFacade`/`RoomFacade` and `RoomManager`.
- `RoomManager` owns the in-memory room registry (`ConcurrentDictionary<long, IRoom>`) and the match-type factory;
  `GetOrLoadRoom` hydrates an existing DB room (head-to-head/team-versus only, rejects ended or foreign-source rooms).
  Creation, hydration, match-type reloads and metadata lookups explicitly use `ServerIdentify.Realtime`; never use
  Stable room records even when their match type/category matches. Rooms persist
  through Common's `IRoomRepository` (both MySQL and PostgreSQL backends); there is **no LegacyIO / osu-web interop**
  call — the lazer API's `/app/router/lio.py` semantics (participant dedup, empty-room end) are implemented directly in
  the repository layer.

### Chat & notifications

- `INotificationServer`/`NotificationServer` (`Notifications/`) is the chat server (the C# counterpart of the lazer API
  `ChatServer` plus osu-web's chat model): per-user connections live in memory, membership/read markers and channels'
  `last_message_id` are persisted through `IChatRepository` (`chat_user_channels`, `chat_channels`), public channel
  activity lives in the Redis sorted set `chat:channel:{channelId}` behind `IRankedCache`
  (`Caching/IRankedCache.cs`, 60 second window refreshed by `POST /chat/ack`), and `message_throttle:{userId}:{PM|PUBLIC}`
  enforces the rate limit. Chat limits come from the optional `config/chat.json` (`ChatConfig` in the Realtime root). It
  never touches SignalR — it broadcasts by calling `IPlayer.OnNewMessage`, `OnChannelJoin`, `OnChannelLeave`,
  `OnUserSilent` and `OnNewNotification`.
- Chat semantics follow osu-web: `addUser` only announces a join when it inserts a row or unhides one, `removeUser`
  hides PM/announcement rows instead of deleting them, a new message updates `last_message_id`, marks the sender's row
  read and unhides the channel, and read markers never move backwards. Broadcast scopes mirror `ChatMessageEvent`:
  channel join/part go to every connection of the acting user, a `!` command goes back to its sender only, public
  channels reach the users that acknowledged them within the activity window, and every other channel reaches its
  membership rows (a PM channel falls back to the two IDs in `pm_{user1}_{user2}`).
- `GET /chat/channels` is osu-web's `ChannelsController::index`: every joinable public channel with the plain
  channel transform and no include. The user's own channels follow osu-web's `UserChannelList`
  (`GET /chat/presence`, `GET /chat/updates`): the visible `chat_user_channels` rows ordered by channel ID, with PM
  conversations of blocked (non-bot) users filtered out by `Channel::isVisibleFor` rules; `/chat/presence` attaches the
  newest messages of each channel (one query, filtered like `Message::filter`: other users' `!` commands and, for public
  channels, anything older than the configured backlog). `ChatChannelDtoFactory` (`Objects/Chat/`) builds every channel
  and message payload (`ChannelsController`, `ChatController`, `MessagesController` and the WebSocket listener share it);
  `ChatPmStartChecker` implements the `ChatPmStart` block/friends-only rules used by `chat/new` and `POST /chat/channels`.
- Messages are validated like `Channel::receiveMessage`: newlines collapse into spaces, blank and over-long messages are
  rejected with `422`, and too-fast senders get `429` through `ChatMessageRejectedException` (`ChatErrorResults` maps it).
- Every `POST` chat endpoint reads its parameters through `ChatRequestParameters` (`Controllers/`), accepting a JSON body,
  a form body or the query string like osu-web's `get_params(request()->all())` — so lazer's form-encoded requests work.
- `LazerWebSocketListener` serves `/notification-server` (mapped in `Program.ConfigureApp`) and maps those callbacks to
  WebSocket frames: `chat.channel.join`/`part` carry the whole channel object and `chat.message.new` carries the message
  plus the sender compact, as in osu-web's `ChatChannelEvent`/`ChatMessageEvent`. WebSocket frames use Newtonsoft.Json
  with a snake_case naming policy, as do IPC payloads and the MVC JSON pipeline (`AddNewtonsoftJson`); the shared
  `Json/RealtimeJson.cs` holds the serializer settings. Chat events are gated behind the client's
  `chat.start`/`chat.end` messages.
- `RoomManager.EnsureRoomChannelAsync` creates the `mp_{roomId}` channel through `IChatRepository` and writes the ID back
  with `IRoomRepository.UpdateRoomChannelId`; rooms add/remove their members through the notification server instead of
  IPC, so any test that creates a room needs `IChatRepository` in its scope factory.
- `ChatIpcHandler` registers the `new_notification` notice, and `BanchoBot` executes `!` commands declared with
  `[BotCommand]` in `Notifications/Commands/`.

Player model:
- `PlayerManager` is a singleton registry keyed by `(playerId, server)`. A player may have multiple `IPlayer` instances per `PlayerId` (one per source server). `GetPlayer<T>(int)` returns the first match across servers; `GetPlayerAllInstances(int)` returns all.
- Hubs install per-hub callback delegates onto the `LazerPlayer` (e.g. `OnUserBeganPlayingForSpectatorHub`, `OnPlayerOnlineForMetadataHub`). `OnDisconnectedAsync` clears them. The hub never holds direct references to other connections — it pushes to `IHubContext<TSelf, TClient>.Clients.User(...)` so reconnects are transparent.
- `PlayerFacade` carries the dependencies a `LazerPlayer` needs (manager, config, score buffer/uploader, score processed notifier). `GetOrCreatePlayer` merges new dependencies into the existing facade via `ApplyNonNullDependenciesFrom`; assume both first-connect and reconnect paths exercise this.
- Watcher tracking (`_playerWatching`) lives on the manager. Broadcast helpers (`BroadcastUserBeganPlaying`, `BroadcastUserSentFrames`, ...) only deliver to current watchers — fan-out is per-event, not per-subscription.

Background services (`Services/`):
- `ScoreBuffer` holds in-flight scores keyed by score token in `IMemoryCache`.
- `ScoreUploader` is a singleton with a bounded number of background workers (`ReplayUploaderConcurrency`, **read once at construction** — restart required to change). It enqueues onto an unbounded `Channel<ReplayUploadItem>` and respects `SaveReplays` per call. Implements `IDisposable`; the test environment must dispose it.
- `ScoreProcessedNotificationService` polls the score repository for processed scores (retry/timeout fields are tunable for tests).

## Tests

NUnit 4 + `Microsoft.NET.Test.Sdk` 17. No real DB/Redis/S3 — tests construct everything in-process:

- `SpectatorHubTests` and `MetadataHubTests` each define a nested `*TestEnvironment` plus fake `IHubContext`, `IHubCallerClients`, `HubCallerContext`, `IGroupManager`, repositories (`Fake*Repository`), `IStorageService`, and IPC transport. When adding a hub test, extend the existing environment rather than instantiating a real `SignalR` pipeline.
- `MultiplayerHubTests` mirrors this pattern with `MultiplayerHubTestInfrastructure` (router + `RecordingMultiplayerClient` + `FakeRoomRepository` + `FakeScopeFactory`). The router aliases a user id to its connection id recorder so unicast (`Clients.User`) and group events land on the same recorder. `MultiplayerIntegrationTests` spins up a real `TestServer` + `HubConnection` with the MessagePack protocol for genuine multi-client collaboration tests (head-to-head full lifecycle, team-versus teams, kick, wrong password); the test host registers `FakeRoomRepository` as `IRoomRepository` and a no-op IPC transport, and the client URL carries `?userId=` for `IUserIdProvider`.
- Each environment writes a throwaway `config/` tree under `Path.GetTempPath()` and points a new `ConfigurationManager` at it. Honor this pattern — `ConfigurationManager` requires real files on disk.
- `ScoreProcessedNotificationService.ScoreLookupTimeout` / `ScoreLookupRetryDelay` are deliberately exposed so tests can shrink them to milliseconds; keep them settable.
- Tests in the test project have `<NoWarn>SA0001;SA1600;SA1602;CS1591;CS0618;MA0048</NoWarn>` — do not "fix" missing doc comments there by adding them.

## Gotchas

- `Hubs/SpectatorHub.cs:364` carries an open `FIXME` about cross-server watchers; the surrounding switch on `notFromThisSource.Length` (0/1/≥2) handles disambiguating duplicate watchers from different source servers by appending `(serverName)` to the username — preserve this logic when touching watcher events.
- `ScoreUploader` increments `_remainingUsages` before enqueueing; if you change the upload pipeline, decrement it in **every** terminal path so `MonitorLoop` can drain on shutdown.
- The `.csproj` `<Content Include="..\.github\workflows\ci.yml">` link is intentional (so the file appears in the IDE solution tree). Don't delete it when cleaning up.
- `config/` and `storage/` are gitignored; the dev workflow expects you to create them locally from the README templates.
