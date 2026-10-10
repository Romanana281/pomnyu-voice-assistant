using Assistant.Application.Commands;
using Assistant.Application.Handling;
using Assistant.Application.Presentation;
using Assistant.Application.Scheduling;

namespace Assistant.Application.Handlers;

public sealed class ListItemsHandler(ScheduleService schedule) : CommandHandler<ListItemsCommand>
{
    protected override async Task<AssistantReply> HandleAsync(
        ListItemsCommand command, ConversationContext context, CancellationToken cancellationToken)
    {
        var occurrences = await schedule.ListOccurrencesAsync(
            context.UserId, command.From, command.To, command.Types, cancellationToken);
        context.State.RememberList(occurrences.Select(occurrence => occurrence.Item.Id));

        var period = PeriodPhrase(command.From, command.To, context.Today);
        if (occurrences.Count == 0)
            return AssistantReply.Done($"{period} ничего не запланировано.");

        var text = $"{period} {ItemPhrases.CountByType(occurrences)}.";
        var nearest = occurrences.FirstOrDefault(occurrence => !occurrence.IsDone && occurrence.Start > context.LocalNow);
        if (command.From == command.To && nearest is not null)
        {
            text += $" Ближайшее — {ItemPhrases.Quoted(nearest.Item)} в {RussianText.Clock(nearest.Time!.Value)}.";
        }
        else if (command.From != command.To)
        {
            var busiest = occurrences.GroupBy(occurrence => occurrence.Date!.Value).MaxBy(day => day.Count())!;
            if (busiest.Count() > 1)
            {
                var heading = RussianText.DayHeading(busiest.Key, context.Today);
                text += $" Самый насыщенный день — {char.ToLowerInvariant(heading[0])}{heading[1..]}.";
            }
        }

        var days = occurrences
            .GroupBy(occurrence => occurrence.Date!.Value)
            .Select(day => new AgendaDay(day.Key, day.Select(occurrence => ItemView.From(occurrence, context.Today)).ToArray()))
            .ToArray();
        return AssistantReply.Done(text, new AgendaCard(command.From, command.To, days));
    }

    private static string PeriodPhrase(DateOnly from, DateOnly to, DateOnly today)
    {
        if (from == to)
            return $"На {RussianText.WhenAccusative(from, today)}";

        return from.Month == to.Month && from.Year == to.Year
            ? $"С {from.Day} по {RussianText.CalendarDate(to, today)}"
            : $"С {RussianText.CalendarDate(from, today)} по {RussianText.CalendarDate(to, today)}";
    }
}
