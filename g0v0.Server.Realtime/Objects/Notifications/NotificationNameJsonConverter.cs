// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace g0v0.Server.Realtime.Objects.Notifications;

internal sealed class NotificationNameJsonConverter : StringEnumConverter
{
    public NotificationNameJsonConverter()
        : base(new SnakeCaseNamingStrategy(), allowIntegerValues: false)
    {
    }
}