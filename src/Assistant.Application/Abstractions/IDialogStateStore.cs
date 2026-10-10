using Assistant.Application.Dialog;

namespace Assistant.Application.Abstractions;

public interface IDialogStateStore
{
    DialogState Get(Guid userId, string conversationId);
}
