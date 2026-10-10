using System.Text;
using Assistant.Application.Abstractions;
using Assistant.Application.Handling;
using Assistant.Application.Presentation;
using Assistant.Application.Scheduling;

namespace Assistant.Application.Understanding;

public sealed class PromptBuilder(ScheduleService schedule)
{
    private const int CalendarDaysAhead = 14;
    private const int MaxListedInPrompt = 10;

    public async Task<LlmRequest> BuildAsync(string userText, ConversationContext context, bool allowSeveral, CancellationToken cancellationToken)
    {
        var system = new LlmMessage(LlmRole.System, await SystemPromptAsync(context, cancellationToken));
        var user = new LlmMessage(LlmRole.User, userText);
        var messages = context.State.History.Prepend(system).Append(user).ToArray();
        return new LlmRequest(messages, AssistantFunctions.For(context, allowSeveral));
    }

    private async Task<string> SystemPromptAsync(ConversationContext context, CancellationToken cancellationToken)
    {
        var dates = new PromptDates(context.Today);
        var prompt = new StringBuilder($"""
            Ты — голосовой ассистент-планировщик. Помогаешь вести задачи, напоминания и встречи.
            Пользователь говорит по-русски, разговорно; текст получен распознаванием речи и может содержать ошибки — понимай по смыслу.

            Как отвечать:
            - Если запрос про задачи, напоминания или встречи — вызови подходящую функцию.
            - Если в реплике несколько разных просьб — вызови several_requests и перечисли их; ни одну просьбу не теряй.
            - Заполняй только то, что пользователь сказал. Никогда не выдумывай время и день: если их не назвали, оставь поля пустыми — система сама переспросит. Задача без названного срока — без date.
            - Если пользователь отвечает на уточняющий вопрос ассистента (например, называет время), повтори исходное действие целиком, дополнив его ответом.
            - Если реплика бессвязна или похожа на ошибку распознавания («а вот это да», обрывок фразы) — ничего не создавай и не ищи, а ответь текстом и попроси повторить.
            - Поиск (search_items) — только когда пользователь явно просит найти запись или спрашивает, когда она была или будет.
            - Если запрос не по теме — ответь текстом в одно-два коротких предложения и подскажи, что умеешь: создавать задачи, напоминания и встречи, показывать планы, искать записи, переносить, удалять и отмечать выполненными.

            Сейчас: {RussianText.DayName(context.LocalNow.DayOfWeek)}, {PromptDates.Iso(context.Today)}, {context.LocalNow:HH:mm}. Часовой пояс: {context.TimeZone.Id}.
            Эта неделя: с {dates.StartOfWeekDate} по {dates.EndOfWeek}. Следующая неделя: с {dates.NextWeekStart} по {dates.NextWeekEnd}.

            Календарь на ближайшие дни:
            """);
        prompt.AppendLine();
        for (var offset = 0; offset < CalendarDaysAhead; offset++)
        {
            var day = context.Today.AddDays(offset);
            var mark = offset switch { 0 => " (сегодня)", 1 => " (завтра)", 2 => " (послезавтра)", _ => "" };
            prompt.AppendLine($"- {RussianText.DayName(day.DayOfWeek)} {PromptDates.Iso(day)}{mark}");
        }

        prompt.AppendLine("""

            Правила дат и времени:
            - Дни недели и относительные дни («завтра», «в пятницу», «на следующей неделе», «до конца недели») передавай словами-ключами из описания полей — дату по ним посчитает система. Не вычисляй такие даты сам.
            - «Через N минут/часов» — поле in_minutes.
            - Дату ГГГГ-ММ-ДД пиши, только если пользователь назвал число месяца; сверяйся с календарём выше.
            """);

        await AppendDialogContextAsync(prompt, context, cancellationToken);
        return prompt.ToString();
    }

    private async Task AppendDialogContextAsync(StringBuilder prompt, ConversationContext context, CancellationToken cancellationToken)
    {
        var state = context.State;
        prompt.AppendLine().AppendLine("Контекст диалога:");

        if (state.LastItemId is { } lastId && await schedule.FindAsync(context.UserId, lastId, cancellationToken) is { } last)
        {
            var when = RussianText.When(last.Date, last.Time, context.Today);
            prompt.AppendLine($"- Последняя обсуждавшаяся запись («last»): {RussianText.TypeName(last.Type)} «{last.Title}», {when}.");
        }

        if (state.LastListedIds.Count > 1)
        {
            prompt.AppendLine("- Последний показанный список (номера для ссылок «первую», «вторую»):");
            foreach (var (itemId, index) in state.LastListedIds.Take(MaxListedInPrompt).Select((id, index) => (id, index)))
            {
                if (await schedule.FindAsync(context.UserId, itemId, cancellationToken) is { } item)
                    prompt.AppendLine($"  {index + 1}. {RussianText.TypeName(item.Type)} «{item.Title}»");
            }
        }

        if (state.PendingConfirmation is { } pending)
            prompt.AppendLine($"- Ассистент ждёт ответа на вопрос: «{pending.Question}». Короткое согласие, «оставь» или отказ — вызови {AssistantFunctions.Confirm}. Если пользователь называет другое время или день для этой же новой записи — заново вызови функцию создания с новым временем; не переноси другие записи.");
    }
}
