namespace Assistant.Application.Abstractions;

public class LlmUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
