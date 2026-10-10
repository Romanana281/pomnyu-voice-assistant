using Assistant.Application.Abstractions;
using Assistant.Application.Commands;

namespace Assistant.Application.Dialog;

public sealed class DialogState
{
    public const int MaxHistoryMessages = 10;

    private readonly List<LlmMessage> _history = [];
    private IReadOnlyList<Guid> _lastListedIds = [];

    public Guid? LastItemId { get; private set; }

    public IReadOnlyList<Guid> LastListedIds => _lastListedIds;

    public PendingConfirmation? PendingConfirmation { get; private set; }

    public IReadOnlyList<LlmMessage> History => _history;

    public string? QuestionContext { get; private set; }

    public IDraftCommand? PendingDraft { get; private set; }

    public void RememberDraft(IDraftCommand? draft) => PendingDraft = draft;

    public void RememberQuestionContext(string? text) => QuestionContext = text;

    public void RememberItem(Guid itemId) => LastItemId = itemId;

    public void RememberList(IEnumerable<Guid> itemIds)
    {
        _lastListedIds = itemIds.Distinct().ToArray();
        if (_lastListedIds.Count == 1)
            LastItemId = _lastListedIds[0];
    }

    public void AskConfirmation(PendingConfirmation pending) => PendingConfirmation = pending;

    public void ClearPendingConfirmation() => PendingConfirmation = null;

    public void AppendExchange(string userText, string assistantText)
    {
        _history.Add(new LlmMessage(LlmRole.User, userText));
        _history.Add(new LlmMessage(LlmRole.Assistant, assistantText));
        if (_history.Count > MaxHistoryMessages)
            _history.RemoveRange(0, _history.Count - MaxHistoryMessages);
    }
}

public enum ConfirmChoice
{
    Accept,
    Alternative,
    Reject,
}

public sealed record PendingConfirmation(
    string Question,
    AssistantCommand Accept,
    AssistantCommand? Alternative,
    string RejectReply,
    IReadOnlyDictionary<string, ConfirmChoice> QuickReplies,
    IDraftCommand? Draft = null)
{
    public AssistantCommand? CommandFor(ConfirmChoice choice) => choice switch
    {
        ConfirmChoice.Accept => Accept,
        ConfirmChoice.Alternative => Alternative,
        _ => null,
    };
}
