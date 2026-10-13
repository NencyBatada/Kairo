namespace Kairo.Modules.Identity.SignIn;

internal static class ReturnUrls
{
    public static string Sanitize(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl) || returnUrl[0] != '/')
        {
            return "/";
        }

        if (returnUrl.Length > 1 && (returnUrl[1] == '/' || returnUrl[1] == '\\'))
        {
            return "/";
        }

        return returnUrl.Any(char.IsControl) ? "/" : returnUrl;
    }
}
