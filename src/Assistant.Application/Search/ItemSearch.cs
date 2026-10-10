using System.Numerics.Tensors;
using Assistant.Application.Abstractions;
using Assistant.Application.Commands;
using Assistant.Application.Scheduling;
using Assistant.Domain;
using Microsoft.Extensions.Logging;

namespace Assistant.Application.Search;

public sealed record SearchHit(ScheduleItem Item, double Score, double LexicalScore)
{
    public bool IsExact => LexicalScore >= 0.999;
}

public sealed class ItemSearch(
    IScheduleRepository repository,
    IEmbeddingProvider embeddings,
    ScheduleService schedule,
    ILogger<ItemSearch> logger)
{
    private const double SemanticWeight = 0.75;
    private const double LexicalWeight = 0.25;

    private const double MinScore = 0.5;
    private const double RelatedWordWeight = 0.75;
    private const int MaxHits = 10;

    private const double MaxGapFromBest = 0.15;

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        Guid userId,
        string query,
        IReadOnlySet<ItemType> types,
        TimeScope scope,
        DateOnly today,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? relatedWords = null)
    {
        var items = (await repository.ListAllAsync(userId, cancellationToken))
            .Where(item => types.Contains(item.Type) && IsInScope(item, scope, today))
            .ToArray();
        if (items.Length == 0)
            return [];

        var queryVector = await TryEmbedQueryAsync(query, items, cancellationToken);

        var hits = items
            .Select(item => Hit(query, relatedWords ?? [], queryVector, item))
            .Where(hit => hit.Score >= MinScore)
            .OrderByDescending(hit => hit.Score)
            .ToArray();
        if (hits.Length == 0)
            return [];

        var best = hits[0].Score;
        return hits.TakeWhile(hit => best - hit.Score <= MaxGapFromBest).Take(MaxHits).ToArray();
    }

    private async Task<float[]?> TryEmbedQueryAsync(
        string query, IReadOnlyList<ScheduleItem> items, CancellationToken cancellationToken)
    {
        if (!embeddings.IsAvailable)
            return null;

        await schedule.TryEmbedAsync(items, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        try
        {
            var vectors = await embeddings.EmbedAsync([query], cancellationToken);
            return vectors[0];
        }
        catch (LlmUnavailableException exception)
        {
            logger.LogWarning("Эмбеддинг запроса не посчитан, ищем по словам: {Reason}", exception.Message);
            return null;
        }
    }

    private static SearchHit Hit(string query, IReadOnlyList<string> relatedWords, float[]? queryVector, ScheduleItem item)
    {
        var exact = TextMatcher.Score(query, item.Title);
        var related = relatedWords.Any(word => TextMatcher.Score(word, item.Title) > 0) ? RelatedWordWeight : 0;
        var lexical = Math.Max(exact, related);
        if (queryVector is null || item.Embedding is null || item.Embedding.Length != queryVector.Length)
            return new SearchHit(item, lexical, exact);

        var semantic = TensorPrimitives.CosineSimilarity(queryVector, item.Embedding);
        return new SearchHit(item, Math.Max(lexical, SemanticWeight * semantic + LexicalWeight * lexical), exact);
    }

    private static bool IsInScope(ScheduleItem item, TimeScope scope, DateOnly today) => scope switch
    {
        TimeScope.Any => true,
        TimeScope.Past => item.Date < today || item.Status == ItemStatus.Done,
        TimeScope.Future => item.Date is null || item.Date >= today || item.IsRecurring,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
    };
}
