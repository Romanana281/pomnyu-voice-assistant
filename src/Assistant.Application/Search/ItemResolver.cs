using Assistant.Application.Commands;
using Assistant.Application.Handling;
using Assistant.Application.Scheduling;
using Assistant.Domain;

namespace Assistant.Application.Search;

public abstract record Resolution
{
    public sealed record Found(ScheduleItem Item) : Resolution;

    public sealed record Ambiguous(IReadOnlyList<ScheduleItem> Candidates) : Resolution;

    public sealed record NotFound(string Reason) : Resolution;
}

public sealed class ItemResolver(ScheduleService schedule, ItemSearch search)
{
    private const int MaxCandidates = 5;

    private const double ConfidentLead = 0.1;

    private static readonly IReadOnlySet<ItemType> AllTypes = Enum.GetValues<ItemType>().ToHashSet();

    public async Task<Resolution> ResolveAsync(
        ItemRef target, ConversationContext context, CancellationToken cancellationToken) => target switch
    {
        ItemRef.ById byId => await FindByIdAsync(byId.Id, context, cancellationToken),
        ItemRef.LastMentioned => context.State.LastItemId is { } lastId
            ? await FindByIdAsync(lastId, context, cancellationToken)
            : new Resolution.NotFound("Не понял, о какой записи речь. Назовите её, например: «встречу с Иваном»."),
        ItemRef.ListPosition position => await FindByPositionAsync(position.Number, context, cancellationToken),
        ItemRef.Description description => await FindByDescriptionAsync(description.Text, context, cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
    };

    private async Task<Resolution> FindByIdAsync(Guid itemId, ConversationContext context, CancellationToken cancellationToken)
    {
        var item = await schedule.FindAsync(context.UserId, itemId, cancellationToken);
        return item is { Status: not ItemStatus.Cancelled }
            ? new Resolution.Found(item)
            : new Resolution.NotFound("Эта запись уже удалена.");
    }

    private async Task<Resolution> FindByPositionAsync(int number, ConversationContext context, CancellationToken cancellationToken)
    {
        var listed = context.State.LastListedIds;
        return number >= 1 && number <= listed.Count
            ? await FindByIdAsync(listed[number - 1], context, cancellationToken)
            : new Resolution.NotFound($"В последнем списке нет пункта номер {number}.");
    }

    private async Task<Resolution> FindByDescriptionAsync(string description, ConversationContext context, CancellationToken cancellationToken)
    {
        var hits = await search.SearchAsync(
            context.UserId, description, AllTypes, TimeScope.Any, context.Today, cancellationToken);

        return hits switch
        {
            [] => new Resolution.NotFound($"Не нашёл запись «{description}». Уточните, пожалуйста."),
            [var single] => new Resolution.Found(single.Item),
            [var first, var second, ..] when first.Score - second.Score >= ConfidentLead => new Resolution.Found(first.Item),
            _ => new Resolution.Ambiguous(hits.Take(MaxCandidates).Select(hit => hit.Item).ToArray()),
        };
    }
}
