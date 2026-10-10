namespace Assistant.Application.Abstractions;

public interface IEmbeddingProvider
{
    bool IsAvailable { get; }

    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
}
