// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using OsuScore = osu.Game.Scoring.Score;

namespace g0v0.Server.Realtime.Objects;

/// <summary>
/// Represents a queued spectator replay upload.
/// </summary>
/// <param name="Token">The score token ID.</param>
/// <param name="Score">The score containing replay data.</param>
/// <param name="Beatmap">The beatmap metadata associated with the score.</param>
/// <param name="CancellationToken">The cancellation source controlling upload timeout.</param>
public record ReplayUploadItem(long Token, OsuScore Score, Beatmap Beatmap, CancellationTokenSource CancellationToken) : IDisposable
{
    /// <inheritdoc />
    public void Dispose()
    {
        CancellationToken.Dispose();
        GC.SuppressFinalize(this);
    }
}