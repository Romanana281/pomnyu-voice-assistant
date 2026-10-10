using Assistant.Application.Dialog;

namespace Assistant.Application.Handling;

public sealed record ConversationContext(Guid UserId, DateTime LocalNow, TimeZoneInfo TimeZone, DialogState State)
{
    public DateOnly Today => DateOnly.FromDateTime(LocalNow);
}
