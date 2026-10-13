using Kairo.Modules.Identity.Sessions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Kairo.Modules.Identity.SignIn;

internal static class SignInSetup
{
    public static void AddKairoSignIn(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SessionTimeouts>().BindConfiguration("Kairo:Sessions");
        services.AddSingleton<DatabaseTicketStore>();
        services.AddScoped<GoogleAccountSignIn>();
        services.AddAuthorization();

        var authentication = services.AddAuthentication(IdentitySchemes.Session)
            .AddCookie(IdentitySchemes.Session, options =>
            {
                options.Cookie.Name = IdentitySchemes.SessionCookie;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Path = "/";
                options.SlidingExpiration = true;
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        if (!string.IsNullOrEmpty(configuration["Authentication:Google:ClientId"]))
        {
            authentication.AddOpenIdConnect(IdentitySchemes.Google, options =>
            {
                options.Authority = "https://accounts.google.com";
                options.CallbackPath = IdentitySchemes.CallbackPath;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.ResponseMode = OpenIdConnectResponseMode.Query;
                options.UsePkce = true;
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("email");
                options.Scope.Add("profile");
                options.Prompt = "select_account";
                options.SignInScheme = IdentitySchemes.Session;
                options.MapInboundClaims = false;
                options.SaveTokens = false;
                options.GetClaimsFromUserInfoEndpoint = false;
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.NonceCookie.SameSite = SameSiteMode.Lax;
                options.TokenValidationParameters.ValidIssuers = ["https://accounts.google.com", "accounts.google.com"];
                options.Events.OnTokenValidated = async context =>
                {
                    var signIn = context.HttpContext.RequestServices.GetRequiredService<GoogleAccountSignIn>();
                    var principal = await signIn.SignInAsync(GoogleAccount.From(context.Principal!), context.HttpContext.RequestAborted);
                    if (principal is null)
                    {
                        context.Fail(GoogleAccountSignIn.UnverifiedEmail);
                        return;
                    }

                    context.Principal = principal;
                };
                options.Events.OnRemoteFailure = context =>
                {
                    var error = context.Failure?.Message == GoogleAccountSignIn.UnverifiedEmail ? "unverified_email" : "sign_in_failed";
                    context.Response.Redirect($"/sign-in?error={error}");
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            });
        }

        services.AddOptions<CookieAuthenticationOptions>(IdentitySchemes.Session)
            .Configure<DatabaseTicketStore, IOptions<SessionTimeouts>, TimeProvider>((options, store, sessions, time) =>
            {
                options.SessionStore = store;
                options.ExpireTimeSpan = sessions.Value.IdleTimeout;
                options.TimeProvider = time;
            });

        services.AddOptions<OpenIdConnectOptions>(IdentitySchemes.Google)
            .Configure<IConfiguration, TimeProvider>((options, configuration, time) =>
            {
                options.ClientId = configuration["Authentication:Google:ClientId"];
                options.ClientSecret = configuration["Authentication:Google:ClientSecret"];
                options.TimeProvider = time;
                var origin = (configuration["Kairo:PublicOrigin"] ?? "").TrimEnd('/');
                options.Events.OnRedirectToIdentityProvider = context =>
                {
                    if (origin.Length > 0)
                    {
                        context.ProtocolMessage.RedirectUri = origin + IdentitySchemes.CallbackPath;
                    }

                    return Task.CompletedTask;
                };
            });
    }
}
