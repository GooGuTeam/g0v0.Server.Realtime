// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;

namespace g0v0.Server.Realtime.Tests.Notifications;

/// <summary>
/// In-memory <see cref="INotificationRepository"/> used by notification tests.
/// </summary>
internal sealed class FakeNotificationRepository : INotificationRepository
{
    private readonly List<Notification> _notifications = new();
    private readonly List<UserNotification> _userNotifications = new();
    private int _nextNotificationId;

    public IReadOnlyList<Notification> Notifications => _notifications;

    public IReadOnlyList<UserNotification> UserNotifications => _userNotifications;

    public Task<Notification> CreateAsync(Notification notification, IReadOnlyList<int> receiverIds)
    {
        notification.Id = ++_nextNotificationId;
        _notifications.Add(notification);

        foreach (int receiverId in receiverIds)
        {
            _userNotifications.Add(new UserNotification
            {
                Id = _userNotifications.Count + 1,
                NotificationId = notification.Id,
                Notification = notification,
                UserId = receiverId,
                IsRead = false,
            });
        }

        return Task.FromResult(notification);
    }

    public Task<IReadOnlyList<UserNotification>> GetUserNotificationsAsync(int userId, bool unreadOnly, long? maxId, int limit)
    {
        IEnumerable<UserNotification> query = _userNotifications.Where(record => record.UserId == userId);

        if (unreadOnly)
        {
            query = query.Where(record => !record.IsRead);
        }

        if (maxId.HasValue)
        {
            query = query.Where(record => record.NotificationId < maxId.Value);
        }

        IReadOnlyList<UserNotification> records = query
            .OrderByDescending(record => record.NotificationId)
            .Take(limit)
            .ToList();
        return Task.FromResult(records);
    }

    public Task<int> CountUnreadAsync(int userId)
        => Task.FromResult(_userNotifications.Count(record => record.UserId == userId && !record.IsRead));

    public Task<IReadOnlyList<int>> GetNotificationIdsAsync(IReadOnlyList<string> names, string? objectType, long? objectId, int? id)
    {
        IEnumerable<Notification> query = _notifications;

        if (names.Count > 0)
        {
            query = query.Where(notification => names.Contains(notification.Name, StringComparer.OrdinalIgnoreCase));
        }

        if (objectType != null)
        {
            query = query.Where(notification => string.Equals(notification.ObjectType, objectType, StringComparison.Ordinal));
        }

        if (objectId.HasValue)
        {
            query = query.Where(notification => notification.ObjectId == objectId.Value);
        }

        if (id.HasValue)
        {
            query = query.Where(notification => notification.Id == id.Value);
        }

        IReadOnlyList<int> ids = query.Select(notification => notification.Id).ToList();
        return Task.FromResult(ids);
    }

    public Task<int> MarkAsReadAsync(int userId, IReadOnlyList<int> notificationIds)
    {
        List<UserNotification> records = _userNotifications
            .Where(record => record.UserId == userId
                && !record.IsRead
                && notificationIds.Contains(record.NotificationId))
            .ToList();

        foreach (UserNotification record in records)
        {
            record.IsRead = true;
        }

        return Task.FromResult(records.Count);
    }
}