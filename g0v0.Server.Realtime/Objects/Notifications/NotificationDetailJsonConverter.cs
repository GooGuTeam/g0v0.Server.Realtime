// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace g0v0.Server.Realtime.Objects.Notifications;

internal sealed class NotificationDetailJsonConverter : JsonConverter<NotificationDetail>
{
    public override bool CanWrite => false;

    public override NotificationDetail? ReadJson(JsonReader reader, Type objectType, NotificationDetail? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
        {
            return null;
        }

        JObject detail = JObject.Load(reader);
        NotificationDetail notification = CreateNotification(detail, serializer);
        using JsonReader detailReader = detail.CreateReader();
        serializer.Populate(detailReader, notification);
        return notification;
    }

    public override void WriteJson(JsonWriter writer, NotificationDetail? value, JsonSerializer serializer)
    {
        throw new NotSupportedException("This converter is only used to deserialize IPC notification payloads.");
    }

    private static NotificationDetail CreateNotification(JObject detail, JsonSerializer serializer)
    {
        string? name = detail.Value<string>("name");
        return name switch
        {
            "channel_message" => new ChannelMessageNotification(
                detail.Value<int?>("source_user_id") ?? 0,
                detail["receiver_ids"]?.ToObject<List<int>>(serializer) ?? []),
            "user_achievement_unlock" => new UserAchievementUnlockNotification(),
            _ => throw new JsonSerializationException($"Unsupported notification detail name '{name}'."),
        };
    }
}