# AGENTS.md

ASP.NET Core 8 SignalR realtime server for g0v0 v2 (spectating, online status, multiplay). The solution has two projects: `g0v0.Server.Realtime/` (web host) and `g0v0.Server.Realtime.Tests/` (NUnit 4).

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
- Format check (CI gate): `dotnet format --verify-no-changes --verbosity diagnostic`
- Apply formatting: `dotnet format`
- Strict CI-equivalent build: `dotnet build --configuration Release /warnaserror`

CI runs only the `code-style` job: `dotnet format --verify-no-changes` then `dotnet build --configuration Release /warnaserror`. There is **no test job in CI** — tests must be run locally. Format drift or any analyzer warning fails CI even though local debug builds pass.

`global.json` pins SDK `8.0.0` (`rollForward: latestMinor`). `Directory.Build.props` injects StyleCop.Analyzers, Meziantou.Analyzer, Roslynator, `EnforceCodeStyleInBuild`, and `GenerateDocumentationFile` into every project — do not duplicate those in `.csproj`.

## Runtime configuration (not in source control)

`.gitignore` excludes `config/` and `storage/`. The server reads JSON files from `{ContentRoot}/config/`:

- `general.json` — DB + JWT + Redis (`UseLegacyDatabase: true` → MySQL v1 schema, `false` → PostgreSQL v2). Loaded from `Common`.
- `game.json` — feature flags.
- `realtime.json` — `SaveReplays` (`[Reloadable]`), `ReplayUploaderConcurrency` (read once at startup; see `Services/ScoreUploader.cs`).
- `storage.json` — `Local`/`S3`/`R2`.

Config classes use **Newtonsoft.Json**; default file name is the snake_case of the class name (override with `[ConfigurationFile("...")]`). Only properties annotated `[Reloadable]` are refreshed by `Reload<T>()`. See README for full schema and v1→v2 env-var mapping.

## Code style (non-default)

Enforced by `.editorconfig`, `.globalconfig`, `stylecop.json`, and the shared `Directory.Build.props`:

- File header on every `.cs`: `// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.` followed by a blank line.
- `using` directives **outside** the namespace, `System.*` first, no blank lines between groups; file-scoped namespaces; LF; 4 spaces; no trailing newline.
- SA1124 (regions) and SA1309 (`_underscore` fields) are intentionally disabled. `PlayerFacade`/`IPlayerFacade` even use **public `_underscore` properties** as a dependency-injection facade — preserve this pattern; don't rename them.
- XML doc comments are required on public/exposed members (`documentExposedElements: true`); the test project sets `<NoWarn>SA0001;CS1591</NoWarn>` so tests deliberately skip them.
- `IDE0005` (unused usings) and `IDE0055` (formatting) are warnings, promoted to errors in CI via `/warnaserror`.

## Architecture

Entrypoint: `g0v0.Server.Realtime/Program.cs`. Wiring order matters — `ConfigurationManager` (constructed from `IHostEnvironment.ContentRootPath` via `WebPathProvider`) is registered first so `AddRepositories`, `AddStorage`, `AddRedis`, etc. (extension methods from `Common`) can resolve it.

SignalR specifics:
- MessagePack protocol only. JSON is explicitly removed in `Program.cs`'s `ConfigureClientHubOptions` because some union types don't round-trip through JSON.
- `SignalRUnionWorkaroundResolver.OPTIONS` (from `Common`) is required for derived match-state messages.
- `IUserIdProvider` is replaced by `JwtUserIdProvider` (`JwtUserIdProvider.cs`). It reads `sub`/`NameIdentifier` from claims, and **manually parses the JWT from the `Authorization: Bearer ...` header** when the auth middleware hasn't populated claims (which happens in some hub-negotiation paths). `Extensions/HubCallerContextExtensions.GetUserId()` duplicates the same fallback for use inside hubs; mirror changes between the two.
- Endpoints: `/signalr/spectator` (`SpectatorHub`) and `/signalr/metadata` (`MetadataHub`). Both inherit `LazerRealtimeHub<TClient>`.

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
- Each environment writes a throwaway `config/` tree under `Path.GetTempPath()` and points a new `ConfigurationManager` at it. Honor this pattern — `ConfigurationManager` requires real files on disk.
- `ScoreProcessedNotificationService.ScoreLookupTimeout` / `ScoreLookupRetryDelay` are deliberately exposed so tests can shrink them to milliseconds; keep them settable.
- Tests in the test project have `<NoWarn>SA0001;CS1591</NoWarn>` — do not "fix" missing doc comments there by adding them.

## Gotchas

- `Hubs/SpectatorHub.cs:364` carries an open `FIXME` about cross-server watchers; the surrounding switch on `notFromThisSource.Length` (0/1/≥2) handles disambiguating duplicate watchers from different source servers by appending `(serverName)` to the username — preserve this logic when touching watcher events.
- `ScoreUploader` increments `_remainingUsages` before enqueueing; if you change the upload pipeline, decrement it in **every** terminal path so `MonitorLoop` can drain on shutdown.
- The `.csproj` `<Content Include="..\.github\workflows\ci.yml">` link is intentional (so the file appears in the IDE solution tree). Don't delete it when cleaning up.
- `config/` and `storage/` are gitignored; the dev workflow expects you to create them locally from the README templates.
