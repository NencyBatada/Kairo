namespace Kairo.Modules.Identity.SignIn;

internal static class IdentitySchemes
{
    public const string Session = "Kairo.Session";
    public const string Google = "Google";
    public const string SessionCookie = "__Host-kairo-session";
    public const string CallbackPath = "/api/auth/callback/google";
}
