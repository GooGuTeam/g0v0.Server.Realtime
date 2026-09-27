// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Communication;
using g0v0.Server.Realtime.Objects.Notifications;

namespace g0v0.Server.Realtime.Notifications;

/// <summary>
/// Registers the chat-related IPC notice handlers (<c>new_notification</c>)
/// with the inter-process communication client.
/// </summary>
/// <remarks>
/// These handlers mirror the lazer API <c>ChatSubscriber</c>, which listens on
/// Redis pub/sub for notification events from other servers. Room join/leave
/// events are no longer handled over IPC: the multiplayer system calls the
/// notification server directly.
/// </remarks>
public class ChatIpcHandler(
    InterProcessCommunicationClient ipcClient,
    IServiceScopeFactory scopeFactory)
{
    /// <summary>
    /// The <c>new_notification</c> IPC notice name.
    /// </summary>
    public const string NewNotificationNoticeName = "new_notification";

    /// <summary>
    /// Registers the chat IPC notice handlers.
    /// </summary>
    public void Register()
    {
        ipcClient.RegisterNoticeHandler<NotificationDetail>(NewNotificationNoticeName, HandleNewNotificationAsync);
    }

    private async Task HandleNewNotificationAsync(NotificationDetail? detail)
    {
        if (detail == null)
        {
            return;
        }

        using IServiceScope scope = scopeFactory.CreateScope();
        INotificationServer notificationServer = scope.ServiceProvider.GetRequiredService<INotificationServer>();
        await notificationServer.SendNotification(detail);
    }
}