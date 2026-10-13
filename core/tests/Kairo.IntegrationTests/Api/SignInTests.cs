using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Kairo.IntegrationTests.Api;

public sealed class SignInTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Signing_in_with_Google_starts_a_session()
    {
        var user = GoogleUser.Create("Priya Shah");

        var browser = await _api.SignInAsync(user);
        var me = await browser.GetFromJsonAsync<Me>("/api/me", Token);

        Assert.Equal(("Priya Shah", user.Email), (me!.Name, me.Email));
    }

    [Fact]
    public async Task Emails_are_stored_in_lowercase()
    {
        var user = GoogleUser.Create() with { Email = $"Mixed.Case-{Guid.NewGuid():N}@Example.com" };

        var browser = await _api.SignInAsync(user);
        var me = await browser.GetFromJsonAsync<Me>("/api/me", Token);

        Assert.Equal(user.Email.ToLowerInvariant(), me!.Email);
    }

    [Fact]
    public async Task The_challenge_asks_Google_for_a_code_with_pkce_and_the_public_redirect_uri()
    {
        var browser = _api.CreateBrowser();

        var response = await browser.GetAsync("/api/auth/sign-in?returnUrl=/acme/today", Token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("accounts.google.com", location.Host);
        Assert.Equal(FakeGoogle.ClientId, query["client_id"]);
        Assert.Equal($"{KairoApiFactory.PublicOrigin}/api/auth/callback/google", query["redirect_uri"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("openid email profile", query["scope"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("select_account", query["prompt"]);
    }

    [Fact]
    public async Task The_code_is_redeemed_with_the_public_redirect_uri()
    {
        await _api.SignInAsync(GoogleUser.Create());

        var request = Assert.Single(_api.Google.TokenRequests);
        Assert.Equal($"{KairoApiFactory.PublicOrigin}/api/auth/callback/google", request["redirect_uri"]);
        Assert.True(request.ContainsKey("code_verifier"));
    }

    [Fact]
    public async Task Signing_in_returns_to_the_page_the_user_started_from()
    {
        var location = await _api.SignInAsync(_api.CreateBrowser(), GoogleUser.Create(), "/acme/today?tab=open");

        Assert.Equal("/acme/today?tab=open", location);
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example/")]
    [InlineData("evil")]
    [InlineData("")]
    public async Task Return_addresses_outside_Kairo_fall_back_to_the_home_page(string returnUrl)
    {
        var location = await _api.SignInAsync(_api.CreateBrowser(), GoogleUser.Create(), returnUrl);

        Assert.Equal("/", location);
    }

    [Fact]
    public async Task An_account_with_an_unverified_email_cannot_sign_in()
    {
        var browser = _api.CreateBrowser();

        var location = await _api.SignInAsync(browser, GoogleUser.Create(emailVerified: false), "/");

        Assert.Equal("/sign-in?error=unverified_email", location);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/me", Token)).StatusCode);
    }

    [Fact]
    public async Task Signing_in_again_updates_the_same_user()
    {
        var user = GoogleUser.Create("Old Name");
        var first = await (await _api.SignInAsync(user)).GetFromJsonAsync<Me>("/api/me", Token);

        var renamed = user with { Name = "New Name", Email = $"new-{Guid.NewGuid():N}@example.com" };
        var second = await (await _api.SignInAsync(renamed)).GetFromJsonAsync<Me>("/api/me", Token);

        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal(("New Name", renamed.Email), (second.Name, second.Email));
    }

    [Fact]
    public async Task Me_needs_a_session()
    {
        var response = await _api.CreateBrowser().GetAsync("/api/me", Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Signing_out_ends_the_session_for_every_copy_of_the_cookie()
    {
        var browser = await _api.SignInAsync(GoogleUser.Create());
        var copied = _api.CookieHeader(browser);

        var signOut = await browser.PostAsync("/api/auth/sign-out", null, Token);

        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        var thief = _api.CreateBrowser();
        thief.DefaultRequestHeaders.Add("Cookie", copied);
        Assert.Equal(HttpStatusCode.Unauthorized, (await thief.GetAsync("/api/me", Token)).StatusCode);
    }

    [Fact]
    public async Task Signing_out_needs_the_request_header()
    {
        var browser = _api.CreateBrowser(withRequestHeader: false);

        var response = await browser.PostAsync("/api/auth/sign-out", null, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_session_ends_after_three_idle_days()
    {
        var browser = await _api.SignInAsync(GoogleUser.Create());

        _api.Clock.Advance(TimeSpan.FromDays(3) + TimeSpan.FromMinutes(1));

        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/me", Token)).StatusCode);
    }

    [Fact]
    public async Task An_active_session_ends_fourteen_days_after_sign_in()
    {
        var browser = await _api.SignInAsync(GoogleUser.Create());

        for (var day = 2; day <= 12; day += 2)
        {
            _api.Clock.Advance(TimeSpan.FromDays(2));
            Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/me", Token)).StatusCode);
        }

        _api.Clock.Advance(TimeSpan.FromDays(2) + TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/me", Token)).StatusCode);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private sealed record Me(Guid Id, string Name, string Email, string? PictureUrl);
}
