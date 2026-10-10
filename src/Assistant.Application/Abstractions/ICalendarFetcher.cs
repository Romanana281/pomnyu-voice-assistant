namespace Assistant.Application.Abstractions;

public interface ICalendarFetcher
{
    Task<string> FetchAsync(Uri url, CancellationToken cancellationToken);
}

public sealed class CalendarFetchException(string message, Exception? innerException = null)
    : Exception(message, innerException);
