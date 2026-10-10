using Assistant.Application.Commands;
using Assistant.Application.Handling;
using Assistant.Application.Presentation;
using Assistant.Application.Search;
using Assistant.Domain;

namespace Assistant.Application.Handlers;

public sealed class SearchItemsHandler(ItemSearch search) : CommandHandler<SearchItemsCommand>
{
    private static readonly IReadOnlySet<ItemType> AllTypes = Enum.GetValues<ItemType>().ToHashSet();

    protected override async Task<AssistantReply> HandleAsync(
        SearchItemsCommand command, ConversationContext context, CancellationToken cancellationToken)
    {
        var hits = await search.SearchAsync(
            context.UserId, command.Query, command.Types, TimeScope.Any, context.Today, cancellationToken, command.RelatedWords);
        if (hits.Count == 0 && command.Types.Count < AllTypes.Count)
        {
            hits = await search.SearchAsync(
                context.UserId, command.Query, AllTypes, TimeScope.Any, context.Today, cancellationToken, command.RelatedWords);
        }

        if (hits.Count == 0)
            return AssistantReply.Failure($"Не нашёл ничего похожего на «{command.Query}». Уточните, о чём была запись или когда её ставили.");

        return command.Scope == TimeScope.Past
            ? Timeline(command, hits, context)
            : HitList(command, hits, context);
    }

    private static AssistantReply HitList(SearchItemsCommand command, IReadOnlyList<SearchHit> hits, ConversationContext context)
    {
        var ordered = command.Scope == TimeScope.Future
            ? hits.OrderBy(hit => IsPast(hit.Item, context.Today)).ToArray()
            : hits.ToArray();
        context.State.RememberList(ordered.Select(hit => hit.Item.Id));

        var views = ordered.Select(hit => View(hit, command, context)).ToArray();
        var first = ordered[0];
        var firstOccurrence = first.Item.NextOccurrence(context.Today);
        var text = ordered.Length == 1
            ? $"Нашёл {RussianText.TypeNameAccusative(first.Item.Type)} {ItemPhrases.Describe(firstOccurrence, context.Today)}."
            : first.IsExact && !ordered[1].IsExact
                ? $"Нашёл {RussianText.Plural(ordered.Length, "запись", "записи", "записей")}. Скорее всего, вы про первую — {ItemPhrases.Describe(firstOccurrence, context.Today)}."
                : $"Нашёл {RussianText.Plural(ordered.Length, "запись", "записи", "записей")}. Посмотрите, какая из них нужна.";

        return AssistantReply.Done(text, new SearchCard(SearchLayout.Hits, views));
    }

    private static AssistantReply Timeline(SearchItemsCommand command, IReadOnlyList<SearchHit> hits, ConversationContext context)
    {
        var dated = hits
            .Where(hit => hit.Item.Date is not null)
            .OrderBy(hit => hit.Item.Date)
            .ThenBy(hit => hit.Item.Time)
            .ToArray();
        if (dated.Length == 0)
            return HitList(command, hits, context);

        var past = dated.LastOrDefault(hit => IsPast(hit.Item, context.Today) && !hit.Item.IsRecurring);
        var next = dated
            .Select(hit => hit.Item.NextOccurrence(context.Today))
            .Where(occurrence => occurrence.Date >= context.Today)
            .OrderBy(occurrence => occurrence.Date)
            .FirstOrDefault();

        var parts = new List<string>();
        if (past is not null)
            parts.Add($"Последняя была {RussianText.When(past.Item.Date, past.Item.Time, context.Today)} — {ItemPhrases.Quoted(past.Item)}.");
        else
            parts.Add("Прошедших записей не нашёл.");
        if (next is not null)
            parts.Add($"{(past is null ? "Ближайшая" : "Следующая")} — {RussianText.When(next.Date, next.Time, context.Today)}.");

        context.State.RememberList(dated.Select(hit => hit.Item.Id));
        var views = dated.Select(hit => View(hit, command, context)).ToArray();
        return AssistantReply.Done(string.Join(" ", parts), new SearchCard(SearchLayout.Timeline, views));
    }

    private static SearchHitView View(SearchHit hit, SearchItemsCommand command, ConversationContext context) =>
        new(
            ItemView.From(hit.Item, context.Today),
            hit.IsExact,
            TextMatcher.MatchedWords(string.Join(' ', command.RelatedWords.Prepend(command.Query)), hit.Item.Title));

    private static bool IsPast(ScheduleItem item, DateOnly today) => item.Date < today;
}
