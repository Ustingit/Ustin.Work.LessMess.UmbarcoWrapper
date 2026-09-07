using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Ustin.Work.LessMess.UmbarcoWrapper.Web.Services;
using Xunit;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Web.Tests;

public sealed class BackofficeOAuthClientTests
{
    private static readonly Guid GeneralId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SaveId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private sealed class RouteHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _responder;

        public RouteHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder) => _responder = responder;

        public List<(string Uri, string Body, string? Auth, string? Cookie)> Seen { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Seen.Add((
                request.RequestUri!.ToString(),
                body,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("Cookie", out var c) ? string.Join("; ", c) : null));
            return _responder(request, body);
        }
    }

    private static (BackofficeOAuthClient Client, RouteHandler Handler) NewClient(
        Func<HttpRequestMessage, string, HttpResponseMessage> responder)
    {
        var handler = new RouteHandler(responder);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://umbraco.local/") };
        var options = new WrapperOptions { UmbracoManagementBaseUrl = "https://umbraco.local" };
        return (new BackofficeOAuthClient(http, options, NullLogger<BackofficeOAuthClient>.Instance), handler);
    }

    private static HttpResponseMessage WithCookies(HttpStatusCode status, params string[] cookies)
    {
        var response = new HttpResponseMessage(status);
        foreach (var c in cookies)
        {
            response.Headers.TryAddWithoutValidation("Set-Cookie", c);
        }

        return response;
    }

    [Fact]
    public async Task Login_accumulates_the_bff_cookie_set_across_login_authorize_and_token()
    {
        (BackofficeOAuthClient client, RouteHandler handler) = NewClient((req, _) =>
        {
            var path = req.RequestUri!.AbsolutePath;

            if (path.EndsWith("/security/back-office/login"))
            {
                return WithCookies(HttpStatusCode.OK, "UMB_UCONTEXT=ctxval; path=/; httponly");
            }

            if (path.EndsWith("/security/back-office/authorize"))
            {
                HttpResponseMessage redirect = WithCookies(HttpStatusCode.Found, "umbPkceCode=pkceval; path=/; httponly");
                redirect.Headers.Location = new Uri("https://umbraco.local/umbraco/oauth_complete?code=%5Bredacted%5D&state=xyz");
                return redirect;
            }

            if (path.EndsWith("/security/back-office/token"))
            {
                HttpResponseMessage token = WithCookies(
                    HttpStatusCode.OK,
                    "umbAccessToken=atval; path=/; httponly",
                    "umbRefreshToken=rtval; path=/; httponly");
                token.Content = JsonContent.Create(new { access_token = "[redacted]", refresh_token = "[redacted]", expires_in = 3600 });
                return token;
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        BackofficeLoginResult result = await client.LoginAsync("admin", "pw");

        Assert.True(result.Ok);
        Assert.Contains("UMB_UCONTEXT=ctxval", result.Session!.Cookies);
        Assert.Contains("umbPkceCode=pkceval", result.Session.Cookies);
        Assert.Contains("umbAccessToken=atval", result.Session.Cookies);
        Assert.Contains("umbRefreshToken=rtval", result.Session.Cookies);
        Assert.True(result.Session.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(50));

        var authorize = handler.Seen.Single(s => s.Uri.Contains("/authorize"));
        Assert.Contains("UMB_UCONTEXT=ctxval", authorize.Cookie);
        Assert.Contains("code_challenge=", authorize.Uri);

        var token = handler.Seen.Single(s => s.Uri.EndsWith("/token"));
        Assert.Contains("umbPkceCode=pkceval", token.Cookie);        // PKCE cookie replayed
        Assert.Contains("code=%5Bredacted%5D", token.Body);          // redacted code passed through
        Assert.Contains("code_verifier=", token.Body);
    }

    [Fact]
    public async Task Login_maps_402_to_two_factor_required()
    {
        (BackofficeOAuthClient client, _) = NewClient((_, _) => new HttpResponseMessage(HttpStatusCode.PaymentRequired));

        BackofficeLoginResult result = await client.LoginAsync("admin", "pw");

        Assert.False(result.Ok);
        Assert.True(result.TwoFactorRequired);
    }

    [Fact]
    public async Task Login_fails_clearly_when_authorize_does_not_redirect()
    {
        (BackofficeOAuthClient client, _) = NewClient((req, _) =>
            req.RequestUri!.AbsolutePath.EndsWith("/login")
                ? WithCookies(HttpStatusCode.OK, "UMB_UCONTEXT=x; path=/")
                : new HttpResponseMessage(HttpStatusCode.OK)); // authorize returns 200 (e.g. a login page)

        BackofficeLoginResult result = await client.LoginAsync("admin", "pw");

        Assert.False(result.Ok);
        Assert.Contains("authorize did not redirect", result.Error);
    }

    [Fact]
    public async Task GetTranslations_replays_the_cookie_set_with_a_redacted_bearer_and_builds_group_paths()
    {
        (BackofficeOAuthClient client, RouteHandler handler) = NewClient((req, _) =>
        {
            var path = req.RequestUri!.AbsolutePath;
            var query = req.RequestUri.Query;

            if (path.EndsWith("/dictionary") || (path.Contains("/dictionary") && query.Contains("take=1000")))
            {
                return Json(new
                {
                    total = 2,
                    items = new object[]
                    {
                        new { id = GeneralId, name = "General", parent = (object?)null, translatedIsoCodes = Array.Empty<string>() },
                        new { id = SaveId, name = "General.Save", parent = new { id = GeneralId }, translatedIsoCodes = new[] { "en-US" } },
                    },
                });
            }

            if (path.EndsWith("/language"))
            {
                return Json(new
                {
                    total = 2,
                    items = new object[]
                    {
                        new { isoCode = "en-US", name = "English" },
                        new { isoCode = "de-DE", name = "German" },
                    },
                });
            }

            if (path.EndsWith($"/dictionary/{GeneralId}"))
            {
                return Json(new { id = GeneralId, name = "General", translations = Array.Empty<object>() });
            }

            if (path.EndsWith($"/dictionary/{SaveId}"))
            {
                return Json(new
                {
                    id = SaveId,
                    name = "General.Save",
                    translations = new object[]
                    {
                        new { isoCode = "en-US", translation = "Save" },
                        new { isoCode = "de-DE", translation = "Speichern" },
                    },
                });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        BackofficeTranslationsResponse response = await client.GetTranslationsAsync("umbAccessToken=atval; UMB_UCONTEXT=ctxval");

        Assert.True(response.Ok);
        Assert.Equal(new[] { "de-DE", "en-US" }, response.Data!.Languages);
        Assert.Equal(2, response.Data.Count);

        TranslationItem save = response.Data.Items.Single(i => i.Key == "General.Save");
        Assert.Equal("General", save.Group);
        Assert.Equal("General / General.Save", save.Path);
        Assert.Equal("Speichern", save.Values["de-DE"]);

        Assert.All(handler.Seen, s =>
        {
            Assert.Equal("Bearer [redacted]", s.Auth);
            Assert.Contains("umbAccessToken=atval", s.Cookie);
        });
    }

    [Fact]
    public async Task GetTranslations_maps_401_to_unauthorized()
    {
        (BackofficeOAuthClient client, _) = NewClient((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        BackofficeTranslationsResponse response = await client.GetTranslationsAsync("x=y");

        Assert.False(response.Ok);
        Assert.True(response.Unauthorized);
    }

    private static HttpResponseMessage Json(object payload) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
}
