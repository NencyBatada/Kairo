namespace Kairo.SharedKernel.Agents;

public sealed class AgentCallException(string code, string message, Exception? inner = null) : Exception(message, inner)
{
    public const string NotConfigured = "not_configured";
    public const string Failed = "failed";
    public const string Timeout = "timeout";
    public const string BadAnswer = "bad_answer";

    public string Code { get; } = code;
}
