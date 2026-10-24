// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Net.WebSockets;
using System.Text;
using g0v0.Server.Common.Authentication;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Realtime.Json;
using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Objects.Chat;
using g0v0.Server.Realtime.Objects.Notifications;
using g0v0.Server.Realtime.Objects.Players;
using Newtonsoft.Json;

namespace g0v0.Server.Realtime.Notifications;

/// <summary>
/// An ASP.NET Core WebSocket endpoint implementing the lazer notification
/// server protocol (<c>/notification-server</c>), bound to a
/// <see cref="LazerPlayer"/>.
/// </summary>
/// <remarks>
/// The lazer client connects with a <c>Bearer</c> JWT (header or
/// <c>access_token</c> query parameter), then sends
/// <c>{ "event": "chat.start" }</c> to begin receiving chat events and
/// <c>{ "event": "chat.end" }</c> to stop. The listener binds the
/// <see cref="LazerPlayer"/> chat and notification <c>On...</c> callbacks to
/// WebSocket JSON events (<c>chat.channel.join</c>, <c>chat.channel.part</c>,
/// <c>chat.message.new</c>, <c>new</c>) and pushes them to the connected client.
/// </remarks>
public class LazerWebSocketListener(
    PlayerManager playerManager,
    INotificationServer notificationServer,
    IServiceScopeFactory scopeFactory,
    ILogger<LazerWebSocketListener> logger)
{
    /// <summary>
    /// Wire format of the WebSocket frames. The frames carry the same DTOs the REST
    /// endpoints return, so they are serialized with Newtonsoft.Json: snake_case
    /// property names, enums as their snake_case names, and the DTOs' own
    /// <c>NullValueHandling</c> contract.
    /// </summary>
    private static readonly JsonSerializerSettings JsonSettings = RealtimeJson.SocketFrame;

    /// <summary>
    /// Handles a WebSocket connection.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        string? verifiedUserId = context.User.FindFirst(OAuthClaimTypes.UserId)?.Value;
        if (context.User.Identity?.IsAuthenticated != true || !int.TryParse(verifiedUserId, out int userId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("{\"error\":\"authentication failed\"}");
            return;
        }

        WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
        NotificationConnection connection = new(webSocket);
        LazerPlayer player = LazerPlayer.GetOrCreate(
            playerManager,
            userId,
            new PlayerFacade(playerManager) { Logger = logger, NotificationServer = notificationServer });

        try
        {
            await RunConnectionAsync(connection, player);
        }
        finally
        {
            connection.Dispose();
        }
    }

    /// <summary>
    /// Returns a new handler list with the given handler removed, leaving the
    /// other connections' entries untouched.
    /// </summary>
    /// <typeparam name="THandler">The callback type.</typeparam>
    /// <param name="handlers">The current handler list.</param>
    /// <param name="handler">The handler of the disconnecting connection.</param>
    /// <returns>A new list without the handler.</returns>
    private static List<THandler> WithoutHandler<THandler>(IReadOnlyList<THandler> handlers, THandler handler)
    {
        List<THandler> remaining = [.. handlers];
        remaining.Remove(handler);
        return remaining;
    }

    private async Task SendChannelEventAsync(NotificationConnection connection, LazerPlayer player, string eventName, int channelId)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IChatRepository chatRepository = scope.ServiceProvider.GetRequiredService<IChatRepository>();
        ChatChannel? channel = await chatRepository.GetChannelAsync(channelId);
        if (channel == null)
        {
            return;
        }

        ChatUserChannel? membership = await chatRepository.GetUserChannelAsync(channelId, player.PlayerId);
        ChatChannelDtoFactory dtoFactory = scope.ServiceProvider.GetRequiredService<ChatChannelDtoFactory>();

        await connection.SendEventAsync(eventName, await dtoFactory.CreateChannelAsync(channel, player.PlayerId, membership));
    }

    private async Task<UserCompactDto?> GetSenderAsync(int senderId)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IUserRepository userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        User? sender = await userRepository.GetByIdAsync(senderId);
        return sender == null ? null : UserCompactDto.FromDb(sender);
    }

    /// <summary>
    /// Binds the player's chat callbacks to one WebSocket connection until the
    /// client disconnects, then removes them again.
    /// </summary>
    /// <param name="connection">The connection carrying the frames.</param>
    /// <param name="player">The player bound to the connection.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task RunConnectionAsync(NotificationConnection connection, LazerPlayer player)
    {
        bool chatActive = false;

        Func<ChatMessage, Task> messageHandler = async message =>
        {
            if (!chatActive)
            {
                return;
            }

            UserCompactDto? sender = await GetSenderAsync(message.SenderId);
            await connection.SendEventAsync(
                "chat.message.new",
                new
                {
                    messages = new[] { ChatMessageDto.FromDb(message) },
                    users = sender == null ? [] : new[] { sender },
                });
        };
        Func<int, Task> channelJoinedHandler = channelId => chatActive
            ? SendChannelEventAsync(connection, player, "chat.channel.join", channelId)
            : Task.CompletedTask;
        Func<int, Task> channelLeftHandler = channelId => chatActive
            ? SendChannelEventAsync(connection, player, "chat.channel.part", channelId)
            : Task.CompletedTask;
        Func<int, DateTimeOffset?, Task> userSilencedHandler = (channelId, until) => chatActive
            ? connection.SendEventAsync("chat.user.silenced", new { channel_id = channelId, until })
            : Task.CompletedTask;
        Func<NotificationDetail, Task> notificationHandler = detail =>
            connection.SendEventAsync("new", detail);

        player.ChatMessageReceivedHandlers = [.. player.ChatMessageReceivedHandlers, messageHandler];
        player.ChatChannelJoinedHandlers = [.. player.ChatChannelJoinedHandlers, channelJoinedHandler];
        player.ChatChannelLeftHandlers = [.. player.ChatChannelLeftHandlers, channelLeftHandler];
        player.ChatUserSilencedHandlers = [.. player.ChatUserSilencedHandlers, userSilencedHandler];
        player.NotificationReceivedHandlers = [.. player.NotificationReceivedHandlers, notificationHandler];

        // The chat WebSocket counts as a connection: it keeps the player online
        // exactly like the realtime hubs do.
        await player.ChatConnected();
        await notificationServer.Connect(player);

        try
        {
            await connection.ReadLoopAsync(() => chatActive = true, () => chatActive = false);
        }
        finally
        {
            player.ChatMessageReceivedHandlers = WithoutHandler(player.ChatMessageReceivedHandlers, messageHandler);
            player.ChatChannelJoinedHandlers = WithoutHandler(player.ChatChannelJoinedHandlers, channelJoinedHandler);
            player.ChatChannelLeftHandlers = WithoutHandler(player.ChatChannelLeftHandlers, channelLeftHandler);
            player.ChatUserSilencedHandlers = WithoutHandler(player.ChatUserSilencedHandlers, userSilencedHandler);
            player.NotificationReceivedHandlers = WithoutHandler(player.NotificationReceivedHandlers, notificationHandler);
            await notificationServer.Disconnect(player);
            await player.ChatDisconnected();
        }
    }

    /// <summary>
    /// Owns a single notification WebSocket connection: it serializes outgoing
    /// frames and reads the client's <c>chat.start</c>/<c>chat.end</c> messages.
    /// </summary>
    /// <param name="webSocket">The accepted WebSocket.</param>
    private sealed class NotificationConnection(WebSocket webSocket) : IDisposable
    {
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        /// <summary>
        /// Sends one event frame to the client.
        /// </summary>
        /// <param name="eventName">The event name.</param>
        /// <param name="data">The event payload.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task SendEventAsync(string eventName, object? data)
        {
            string payload = JsonConvert.SerializeObject(new SocketMessage { Event = eventName, Data = data }, JsonSettings);
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            await _sendLock.WaitAsync();
            try
            {
                if (webSocket.State == WebSocketState.Open)
                {
                    await webSocket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
                }
            }
            finally
            {
                _sendLock.Release();
            }
        }

        /// <summary>
        /// Reads client frames until the socket closes.
        /// </summary>
        /// <param name="startChat">Invoked when the client sends <c>chat.start</c>.</param>
        /// <param name="stopChat">Invoked when the client sends <c>chat.end</c>.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task ReadLoopAsync(Action startChat, Action stopChat)
        {
            byte[] buffer = new byte[4096];
            try
            {
                await ReadFramesAsync(buffer, startChat, stopChat);
            }
            catch (WebSocketException exception) when (exception.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                // Clients and intermediaries (mobile clients dying, network
                // switches, CDN/proxy idle timeouts) frequently drop the TCP
                // connection without completing the WebSocket close handshake.
                // That is a normal disconnect, not an error; the cleanup in
                // RunConnectionAsync's finally block still runs.
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _sendLock.Dispose();
            webSocket.Dispose();
        }

        /// <summary>
        /// Reads client frames until the socket closes or a frame error occurs.
        /// </summary>
        /// <param name="buffer">The shared receive buffer.</param>
        /// <param name="startChat">Invoked when the client sends <c>chat.start</c>.</param>
        /// <param name="stopChat">Invoked when the client sends <c>chat.end</c>.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task ReadFramesAsync(byte[] buffer, Action startChat, Action stopChat)
        {
            while (webSocket.State == WebSocketState.Open)
            {
                using MemoryStream stream = new();
                WebSocketReceiveResult result;
                do
                {
                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        // Complete the server side of the close handshake so the
                        // client does not see an aborted connection.
                        try
                        {
                            await webSocket.CloseOutputAsync(
                                WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                        }
                        catch (WebSocketException)
                        {
                            // The peer is already gone; Dispose still cleans up.
                        }

                        return;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        stream.Write(buffer, 0, result.Count);
                    }
                }
                while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                SocketMessage? message;
                try
                {
                    message = JsonConvert.DeserializeObject<SocketMessage>(
                        Encoding.UTF8.GetString(stream.ToArray()),
                        JsonSettings);
                }
                catch (JsonException)
                {
                    continue;
                }

                switch (message?.Event)
                {
                    case "chat.start":
                        startChat();
                        break;
                    case "chat.end":
                        stopChat();
                        break;
                }
            }
        }
    }

    private sealed class SocketMessage
    {
        public string Event { get; set; } = string.Empty;

        public object? Data { get; set; }
    }
}