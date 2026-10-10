using System.Text.Json.Serialization;
using Assistant.Domain;

namespace Assistant.Application.Presentation;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SavedCard), "saved")]
[JsonDerivedType(typeof(ChangedCard), "changed")]
[JsonDerivedType(typeof(DraftCard), "draft")]
[JsonDerivedType(typeof(ConflictCard), "conflict")]
[JsonDerivedType(typeof(AgendaCard), "agenda")]
[JsonDerivedType(typeof(SearchCard), "search")]
[JsonDerivedType(typeof(ChoiceCard), "choice")]
[JsonDerivedType(typeof(ConfirmDeleteCard), "confirmDelete")]
public abstract record ReplyCard
{
    [JsonIgnore]
    public abstract IReadOnlyList<ItemView> Items { get; }
}

public enum SavedAction
{
    Created,
    Completed,
    Deleted,
}

public sealed record SavedCard(ItemView Item, SavedAction Action) : ReplyCard
{
    public override IReadOnlyList<ItemView> Items => [Item];
}

public sealed record ChangedCard(
    ItemView Item,
    DateOnly? PreviousDate,
    TimeOnly? PreviousTime,
    string? PreviousTitle) : ReplyCard
{
    public override IReadOnlyList<ItemView> Items => [Item];
}

public enum MissingField
{
    Date,
    Time,
}

public sealed record DraftCard(
    ItemType ItemType,
    string Title,
    DateOnly? Date,
    TimeOnly? Time,
    MissingField Missing,
    IReadOnlyList<TimeOnly> FreeSlots) : ReplyCard
{
    public override IReadOnlyList<ItemView> Items => [];
}

public sealed record ConflictCard(
    DateOnly Date,
    string Title,
    TimeOnly Start,
    int DurationMinutes,
    TimeOnly? Suggested,
    IReadOnlyList<ItemView> Conflicts) : ReplyCard
{
    public override IReadOnlyList<ItemView> Items => Conflicts;
}

public sealed record AgendaDay(DateOnly Date, IReadOnlyList<ItemView> Items);

public sealed record AgendaCard(DateOnly From, DateOnly To, IReadOnlyList<AgendaDay> Days) : ReplyCard
{
    public override IReadOnlyList<ItemView> Items => Days.SelectMany(day => day.Items).ToArray();
}

public sealed record SearchHitView(ItemView Item, bool IsExact, IReadOnlyList<string> Highlights);

public enum SearchLayout
{
    Hits,
    Timeline,
}

public sealed record SearchCard(SearchLayout Layout, IReadOnlyList<SearchHitView> Hits) : ReplyCard
{
    public override IReadOnlyList<ItemView> Items => Hits.Select(hit => hit.Item).ToArray();
}

public sealed record ChoiceCard(IReadOnlyList<ItemView> Options) : ReplyCard
{
    public override IReadOnlyList<ItemView> Items => Options;
}

public sealed record ConfirmDeleteCard(ItemView Item) : ReplyCard
{
    public override IReadOnlyList<ItemView> Items => [Item];
}
