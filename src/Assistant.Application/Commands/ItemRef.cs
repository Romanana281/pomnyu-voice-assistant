namespace Assistant.Application.Commands;

public abstract record ItemRef
{
    public sealed record LastMentioned : ItemRef;

    public sealed record ListPosition(int Number) : ItemRef;

    public sealed record Description(string Text) : ItemRef;

    public sealed record ById(Guid Id) : ItemRef;
}
