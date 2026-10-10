using System.Text.Json.Nodes;
using Assistant.Application.Abstractions;
using Assistant.Application.Handling;

namespace Assistant.Application.Understanding;

internal static class AssistantFunctions
{
    public const string CreateTask = "create_task";
    public const string CreateMeeting = "create_meeting";
    public const string ListItems = "list_items";
    public const string SearchItems = "search_items";
    public const string UpdateItem = "update_item";
    public const string DeleteItem = "delete_item";
    public const string CompleteItem = "complete_item";
    public const string Confirm = "confirm";
    public const string SeveralRequests = "several_requests";

    private const string DayDescription =
        "День словом: today, tomorrow, day_after_tomorrow; день недели mon, tue, wed, thu, fri, sat, sun — ближайший такой день, включая сегодня («в пятницу» — fri); " +
        "next_mon … next_sun — день следующей недели («в следующую среду» — next_wed); end_of_week — «до конца недели»; next_week — «на следующей неделе»; end_of_month — «до конца месяца». " +
        "Только если названо конкретное число («15 октября») — дата ГГГГ-ММ-ДД. Не заполняй, если день не назван.";

    private const string TimeDescription =
        "Время ЧЧ:ММ в 24-часовом формате, только если названо: «в 10 утра» — 10:00, «в 3 часа дня» — 15:00, «в семь вечера» — 19:00, «в полдень» — 12:00; час без уточнения от 1 до 7 — дневной (13:00–19:00), от 8 до 11 — утренний.";

    private const string InMinutesDescription =
        "Только для «через N минут/часов»: сколько минут от текущего момента («через два часа» — 120). Тогда date и time не заполняй.";

    private const string ItemReference =
        "Какая запись имеется в виду: «last» — если пользователь говорит «её», «его», «эту», «последнюю» о записи, которую только что обсуждали; " +
        "номер из последнего показанного списка, например «2», — если говорит «вторую»; иначе — короткое описание для поиска, например «встреча с Иваном» или «отчёт».";

    private const string RecurrenceSchema = """
        {
          "type": "object",
          "description": "Только если пользователь просит повторять: «каждый день», «по понедельникам», «по будням», «раз в месяц».",
          "properties": {
            "frequency": { "type": "string", "enum": ["daily", "weekly", "monthly"], "description": "Частота повтора." },
            "interval": { "type": "integer", "description": "Каждые N периодов, по умолчанию 1. «Раз в две недели» — 2 для weekly." },
            "days": {
              "type": "array",
              "items": { "type": "string", "enum": ["mo", "tu", "we", "th", "fr", "sa", "su"] },
              "description": "Дни недели для weekly. «По будням» — mo, tu, we, th, fr."
            },
            "until": { "type": "string", "description": "Дата окончания повторов ГГГГ-ММ-ДД, если названа." }
          },
          "required": ["frequency"]
        }
        """;

    private static readonly LlmFunction[] Always =
    [
        Function($$"""
            {
              "name": "{{CreateTask}}",
              "description": "Создать задачу или напоминание: пользователь хочет что-то сделать, не забыть, просит напомнить. «Напомни…» — kind=reminder, иначе kind=task.",
              "parameters": {
                "type": "object",
                "properties": {
                  "title": { "type": "string", "description": "Суть задачи с заглавной буквы, без слов о дате, времени и без «напомни мне», «нужно», «поставь задачу». Например: «Позвонить Ивану», «Купить билеты»." },
                  "kind": { "type": "string", "enum": ["task", "reminder"] },
                  "date": { "type": "string", "description": "{{DayDescription}}" },
                  "time": { "type": "string", "description": "{{TimeDescription}}" },
                  "in_minutes": { "type": "integer", "description": "{{InMinutesDescription}}" },
                  "recurrence": {{RecurrenceSchema}}
                },
                "required": ["title", "kind"]
              },
              "few_shot_examples": [
                { "request": "Напомни мне завтра в 10 утра позвонить Ивану", "params": { "title": "Позвонить Ивану", "kind": "reminder", "date": "tomorrow", "time": "10:00" } },
                { "request": "В пятницу нужно отправить отчет", "params": { "title": "Отправить отчёт", "kind": "task", "date": "fri" } },
                { "request": "Поставь задачу купить билеты до конца недели", "params": { "title": "Купить билеты", "kind": "task", "date": "end_of_week" } },
                { "request": "Через два часа напомни выпить воды", "params": { "title": "Выпить воды", "kind": "reminder", "in_minutes": 120 } },
                { "request": "Каждый будний день в 9 напоминай проверить почту", "params": { "title": "Проверить почту", "kind": "reminder", "date": "today", "time": "09:00", "recurrence": { "frequency": "weekly", "days": ["mo", "tu", "we", "th", "fr"] } } }
              ]
            }
            """),
        Function($$"""
            {
              "name": "{{CreateMeeting}}",
              "description": "Создать встречу, созвон, совещание, звонок с кем-то в конкретное время.",
              "parameters": {
                "type": "object",
                "properties": {
                  "title": { "type": "string", "description": "Название с заглавной буквы, без даты и времени: «Встреча с командой», «Встреча с дизайнером», «Созвон с заказчиком»." },
                  "date": { "type": "string", "description": "{{DayDescription}}" },
                  "time": { "type": "string", "description": "Время начала. {{TimeDescription}} Не выдумывай время, если его не назвали." },
                  "in_minutes": { "type": "integer", "description": "{{InMinutesDescription}}" },
                  "duration_minutes": { "type": "integer", "description": "Длительность в минутах, только если названа: «на полчаса» — 30, «на два часа» — 120." },
                  "recurrence": {{RecurrenceSchema}}
                },
                "required": ["title"]
              },
              "few_shot_examples": [
                { "request": "Добавь встречу с командой завтра в 15:00", "params": { "title": "Встреча с командой", "date": "tomorrow", "time": "15:00" } },
                { "request": "В четверг в 11 у меня встреча с дизайнером", "params": { "title": "Встреча с дизайнером", "date": "thu", "time": "11:00" } },
                { "request": "Поставь встречу с Иваном завтра", "params": { "title": "Встреча с Иваном", "date": "tomorrow" } },
                { "request": "Каждый понедельник в 10 планёрка", "params": { "title": "Планёрка", "date": "mon", "time": "10:00", "recurrence": { "frequency": "weekly", "days": ["mo"] } } }
              ]
            }
            """),
        Function($$"""
            {
              "name": "{{ListItems}}",
              "description": "Показать, что запланировано на период: «что у меня сегодня», «какие планы на завтра», «есть ли встречи», «что на этой неделе». Только про встречи — types=[meeting]; только про задачи — types=[task, reminder]; иначе не заполняй types.",
              "parameters": {
                "type": "object",
                "properties": {
                  "period": { "type": "string", "enum": ["today", "tomorrow", "day_after_tomorrow", "this_week", "next_week", "weekend", "this_month", "next_month", "mon", "tue", "wed", "thu", "fri", "sat", "sun"], "description": "Период словом. Если названы конкретные числа — не заполняй, а заполни from и to." },
                  "from": { "type": "string", "description": "Первый день ГГГГ-ММ-ДД — только если названы конкретные числа." },
                  "to": { "type": "string", "description": "Последний день ГГГГ-ММ-ДД включительно — только если названы конкретные числа." },
                  "types": { "type": "array", "items": { "type": "string", "enum": ["task", "reminder", "meeting"] } }
                }
              },
              "few_shot_examples": [
                { "request": "Что у меня сегодня?", "params": { "period": "today" } },
                { "request": "Какие планы на сегодня?", "params": { "period": "today" } },
                { "request": "Покажи мои встречи на сегодня", "params": { "period": "today", "types": ["meeting"] } },
                { "request": "Какие задачи у меня на завтра?", "params": { "period": "tomorrow", "types": ["task", "reminder"] } },
                { "request": "Что запланировано на эту неделю?", "params": { "period": "this_week" } }
              ]
            }
            """),
        Function($$"""
            {
              "name": "{{SearchItems}}",
              "description": "Найти ранее созданные записи по смыслу, когда пользователь ищет конкретную запись, а не спрашивает про период: «найди задачу про презентацию», «когда у меня была встреча с Иваном».",
              "parameters": {
                "type": "object",
                "properties": {
                  "query": { "type": "string", "description": "Суть искомого без служебных слов: «презентация», «встреча с Иваном», «договор»." },
                  "related_words": { "type": "array", "items": { "type": "string" }, "description": "4–8 синонимов и близких по смыслу слов к сути запроса, которые могли быть в названии записи: для «договор» — контракт, соглашение, подписать, юрист; для «презентация» — слайды, доклад, выступление." },
                  "types": { "type": "array", "items": { "type": "string", "enum": ["task", "reminder", "meeting"] }, "description": "Если ищут именно встречу или именно задачу." },
                  "when": { "type": "string", "enum": ["past", "future", "any"], "description": "«была», «прошлая» — past; «будет», «следующая» — future; иначе any." }
                },
                "required": ["query"]
              },
              "few_shot_examples": [
                { "request": "Найди задачу про презентацию", "params": { "query": "презентация", "related_words": ["слайды", "доклад", "выступление", "питч"], "types": ["task", "reminder"], "when": "any" } },
                { "request": "Когда у меня была встреча с Иваном?", "params": { "query": "Иван", "related_words": ["Ваня", "Иваном", "Ивану"], "types": ["meeting"], "when": "past" } },
                { "request": "Я когда-то ставил задачу по договору, найди ее", "params": { "query": "договор", "related_words": ["контракт", "соглашение", "подписать", "юрист", "оферта"], "types": ["task", "reminder"], "when": "any" } }
              ]
            }
            """),
        Function($$"""
            {
              "name": "{{UpdateItem}}",
              "description": "Изменить существующую запись: перенести на другой день или время, переименовать, изменить длительность. «Перенеси её на завтра», «сдвинь встречу с Иваном на 16».",
              "parameters": {
                "type": "object",
                "properties": {
                  "item": { "type": "string", "description": "{{ItemReference}}" },
                  "new_title": { "type": "string", "description": "Новое название, только если просят переименовать." },
                  "new_date": { "type": "string", "description": "Новый день. {{DayDescription}}" },
                  "new_time": { "type": "string", "description": "Новое время. {{TimeDescription}}" },
                  "new_in_minutes": { "type": "integer", "description": "{{InMinutesDescription}}" },
                  "new_duration_minutes": { "type": "integer", "description": "Новая длительность встречи в минутах." }
                },
                "required": ["item"]
              },
              "few_shot_examples": [
                { "request": "А перенеси ее на завтра", "params": { "item": "last", "new_date": "tomorrow" } },
                { "request": "А перенеси на 11", "params": { "item": "last", "new_time": "11:00" } },
                { "request": "Сдвинь встречу с Иваном на 16:30", "params": { "item": "встреча с Иваном", "new_time": "16:30" } }
              ]
            }
            """),
        Function($$"""
            {
              "name": "{{DeleteItem}}",
              "description": "Удалить или отменить запись. Любая просьба со словами «удали», «убери», «отмени», «вычеркни» — это delete_item, а не поиск: «удали задачу про билеты», «отмени встречу», «удали вторую».",
              "parameters": {
                "type": "object",
                "properties": { "item": { "type": "string", "description": "{{ItemReference}}" } },
                "required": ["item"]
              },
              "few_shot_examples": [
                { "request": "Удали вторую", "params": { "item": "2" } },
                { "request": "Удали задачу про билеты", "params": { "item": "билеты" } },
                { "request": "Отмени встречу с дизайнером", "params": { "item": "встреча с дизайнером" } }
              ]
            }
            """),
        Function($$"""
            {
              "name": "{{CompleteItem}}",
              "description": "Отметить задачу выполненной: «я позвонил Ивану», «отметь отчёт выполненным», «готово».",
              "parameters": {
                "type": "object",
                "properties": { "item": { "type": "string", "description": "{{ItemReference}}" } },
                "required": ["item"]
              },
              "few_shot_examples": [
                { "request": "Отметь задачу про отчёт выполненной", "params": { "item": "отчёт" } },
                { "request": "Сделано", "params": { "item": "last" } }
              ]
            }
            """),
    ];

    private static readonly LlmFunction SeveralRequestsFunction = Function($$"""
        {
          "name": "{{SeveralRequests}}",
          "description": "Разбей реплику пользователя на самостоятельные просьбы в порядке произнесения. Каждая просьба должна быть понятна сама по себе: повтори в ней глагол, день и время, если они к ней относятся. Приветствия и вежливые слова отбрось. Если просьба одна — верни массив из одного элемента.",
          "parameters": {
            "type": "object",
            "properties": { "requests": { "type": "array", "items": { "type": "string" }, "description": "От 2 до 5 самостоятельных запросов." } },
            "required": ["requests"]
          },
          "few_shot_examples": [
            { "request": "Что у меня сегодня и назначь встречу с командой на 17:00", "params": { "requests": ["Что у меня сегодня?", "Назначь встречу с командой сегодня в 17:00"] } },
            { "request": "Напомни завтра в 9 позвонить маме и в пятницу купить подарок", "params": { "requests": ["Напомни завтра в 9 позвонить маме", "Напомни в пятницу купить подарок"] } },
            { "request": "Перенеси встречу с командой на понедельник и удали задачу про билеты", "params": { "requests": ["Перенеси встречу с командой на понедельник", "Удали задачу про билеты"] } },
            { "request": "Что у меня завтра и есть ли встречи в пятницу", "params": { "requests": ["Что у меня завтра?", "Есть ли у меня встречи в пятницу?"] } }
          ]
        }
        """);

    private static readonly LlmFunction ConfirmFunction = Function($$"""
        {
          "name": "{{Confirm}}",
          "description": "Ответ пользователя на вопрос-подтверждение ассистента. accept — согласен с предложением ассистента (удалить, поставить на предложенное время); keep — оставить как просил изначально («оставь в 15:00»); reject — отказ, ничего не делать.",
          "parameters": {
            "type": "object",
            "properties": { "choice": { "type": "string", "enum": ["accept", "keep", "reject"] } },
            "required": ["choice"]
          },
          "few_shot_examples": [
            { "request": "Да, давай", "params": { "choice": "accept" } },
            { "request": "Оставь как я сказал", "params": { "choice": "keep" } },
            { "request": "Нет, не надо", "params": { "choice": "reject" } }
          ]
        }
        """);

    public static LlmFunction Splitter => SeveralRequestsFunction;

    public static IReadOnlyList<LlmFunction> For(ConversationContext context, bool allowSeveral) =>
        (context.State.PendingConfirmation is null, allowSeveral) switch
        {
            (true, true) => [.. Always, SeveralRequestsFunction],
            (true, false) => Always,
            (false, true) => [.. Always, SeveralRequestsFunction, ConfirmFunction],
            (false, false) => [.. Always, ConfirmFunction],
        };

    private static LlmFunction Function(string json)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        var examples = node["few_shot_examples"]!.AsArray()
            .Select(example => new LlmFewShotExample(
                example!["request"]!.GetValue<string>(),
                example["params"]!.AsObject().DeepClone().AsObject()))
            .ToArray();

        return new LlmFunction(
            node["name"]!.GetValue<string>(),
            node["description"]!.GetValue<string>(),
            node["parameters"]!.DeepClone().AsObject(),
            examples);
    }
}
