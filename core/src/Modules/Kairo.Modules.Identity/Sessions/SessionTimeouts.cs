namespace Kairo.Modules.Identity.Sessions;

internal sealed class SessionTimeouts
{
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromDays(3);

    public TimeSpan AbsoluteTimeout { get; set; } = TimeSpan.FromDays(14);
}
