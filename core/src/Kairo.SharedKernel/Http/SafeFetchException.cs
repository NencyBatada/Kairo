namespace Kairo.SharedKernel.Http;

public static class SafeFetchErrors
{
    public const string TooLarge = "too_large";
    public const string Timeout = "timeout";
    public const string BlockedAddress = "blocked_address";
    public const string BadStatus = "bad_status";
    public const string NotHtml = "not_html";
    public const string Unreachable = "unreachable";
}

public sealed class SafeFetchException(string code, string message, Exception? inner = null) : Exception(message, inner)
{
    public string Code { get; } = code;
}
