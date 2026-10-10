using Assistant.Application.Abstractions;
using Assistant.Application.Dialog;
using Microsoft.Extensions.Caching.Memory;

namespace Assistant.Infrastructure.Dialog;

public sealed class MemoryDialogStateStore(IMemoryCache cache) : IDialogStateStore
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    public DialogState Get(Guid userId, string conversationId) =>
        cache.GetOrCreate(Key(userId, conversationId), entry =>
        {
            entry.SlidingExpiration = IdleTimeout;
            return new DialogState();
        })!;

    private static string Key(Guid userId, string conversationId) => $"dialog:{userId}:{conversationId}";
}
