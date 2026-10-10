using Assistant.Application.Abstractions;
using Assistant.Domain;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Infrastructure.Persistence;

public sealed class ScheduleRepository(AssistantDbContext db) : IScheduleRepository
{
    public Task<ScheduleItem?> FindAsync(Guid userId, Guid itemId, CancellationToken cancellationToken) =>
        db.ScheduleItems.FirstOrDefaultAsync(item => item.UserId == userId && item.Id == itemId, cancellationToken);

    public async Task<IReadOnlyList<ScheduleItem>> ListForPeriodAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await Visible(userId)
            .Where(item => item.Date != null && item.Date <= to)
            .Where(item => item.Recurrence != null || item.Date >= from)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ScheduleItem>> ListAllAsync(Guid userId, CancellationToken cancellationToken) =>
        await Visible(userId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ScheduleItem>> ListFromSourceAsync(Guid userId, string source, CancellationToken cancellationToken) =>
        await db.ScheduleItems.Where(item => item.UserId == userId && item.ExternalSource == source).ToListAsync(cancellationToken);

    public void Add(ScheduleItem item) => db.ScheduleItems.Add(item);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    private IQueryable<ScheduleItem> Visible(Guid userId) =>
        db.ScheduleItems.Where(item => item.UserId == userId && item.Status != ItemStatus.Cancelled);
}

public sealed class UserRepository(AssistantDbContext db) : IUserRepository
{
    public Task<AppUser?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public Task<AppUser?> FindByCalendarTokenAsync(string calendarToken, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(user => user.CalendarToken == calendarToken, cancellationToken);

    public Task<AppUser?> FindByLinkTokenAsync(string linkToken, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(user => user.LinkToken == linkToken, cancellationToken);

    public void Add(AppUser user) => db.Users.Add(user);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

public sealed class CalendarSubscriptionRepository(AssistantDbContext db) : ICalendarSubscriptionRepository
{
    public async Task<IReadOnlyList<CalendarSubscription>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.CalendarSubscriptions.Where(subscription => subscription.UserId == userId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CalendarSubscription>> ListAllAsync(CancellationToken cancellationToken) =>
        await db.CalendarSubscriptions.ToListAsync(cancellationToken);

    public Task<CalendarSubscription?> FindAsync(Guid userId, Guid subscriptionId, CancellationToken cancellationToken) =>
        db.CalendarSubscriptions.FirstOrDefaultAsync(
            subscription => subscription.UserId == userId && subscription.Id == subscriptionId, cancellationToken);

    public void Add(CalendarSubscription subscription) => db.CalendarSubscriptions.Add(subscription);

    public void Remove(CalendarSubscription subscription) => db.CalendarSubscriptions.Remove(subscription);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

public sealed class ChatHistoryRepository(AssistantDbContext db) : IChatHistoryRepository
{
    public async Task<IReadOnlyList<ConversationSummary>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var conversations = await db.Conversations
            .Where(conversation => conversation.UserId == userId)
            .OrderByDescending(conversation => conversation.UpdatedAt)
            .ToListAsync(cancellationToken);
        var stats = await db.ChatMessages
            .Where(message => message.UserId == userId)
            .GroupBy(message => message.ConversationId)
            .Select(group => new
            {
                ConversationId = group.Key,
                Count = group.Count(),
                LastText = group.Where(message => message.Role == ChatRole.Assistant)
                    .OrderByDescending(message => message.CreatedAt)
                    .Select(message => message.Text)
                    .FirstOrDefault(),
            })
            .ToDictionaryAsync(stat => stat.ConversationId, cancellationToken);

        return conversations
            .Select(conversation => stats.TryGetValue(conversation.Id, out var stat)
                ? new ConversationSummary(conversation.Id, conversation.Title, conversation.UpdatedAt, stat.Count, stat.LastText)
                : new ConversationSummary(conversation.Id, conversation.Title, conversation.UpdatedAt, 0, null))
            .ToArray();
    }

    public Task<Conversation?> FindAsync(Guid userId, string conversationId, CancellationToken cancellationToken) =>
        db.Conversations.FirstOrDefaultAsync(
            conversation => conversation.UserId == userId && conversation.Id == conversationId, cancellationToken);

    public async Task<IReadOnlyList<ChatMessage>> MessagesAsync(Guid userId, string conversationId, CancellationToken cancellationToken) =>
        await db.ChatMessages
            .Where(message => message.UserId == userId && message.ConversationId == conversationId)
            .OrderBy(message => message.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<ChatMessage?> FindMessageAsync(Guid userId, Guid messageId, CancellationToken cancellationToken) =>
        db.ChatMessages.FirstOrDefaultAsync(message => message.UserId == userId && message.Id == messageId, cancellationToken);

    public void Add(Conversation conversation) => db.Conversations.Add(conversation);

    public void Add(IEnumerable<ChatMessage> messages) => db.ChatMessages.AddRange(messages);

    public async Task RemoveAsync(Guid userId, string conversationId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.ChatMessages
            .Where(message => message.UserId == userId && message.ConversationId == conversationId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.Conversations
            .Where(conversation => conversation.UserId == userId && conversation.Id == conversationId)
            .ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task TrimAsync(Guid userId, string conversationId, int keepMessages, int keepConversations, CancellationToken cancellationToken)
    {
        var overflow = await db.ChatMessages
            .Where(message => message.UserId == userId && message.ConversationId == conversationId)
            .OrderByDescending(message => message.CreatedAt)
            .Skip(keepMessages)
            .Select(message => message.Id)
            .ToListAsync(cancellationToken);
        if (overflow.Count > 0)
            await db.ChatMessages.Where(message => overflow.Contains(message.Id)).ExecuteDeleteAsync(cancellationToken);

        var stale = await db.Conversations
            .Where(conversation => conversation.UserId == userId)
            .OrderByDescending(conversation => conversation.UpdatedAt)
            .Skip(keepConversations)
            .Select(conversation => conversation.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in stale)
            await RemoveAsync(userId, id, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
