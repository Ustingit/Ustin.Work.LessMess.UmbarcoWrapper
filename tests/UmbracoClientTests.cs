using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Ustin.Work.LessMess.UmbarcoWrapper.Web.Services;
using Xunit;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Web.Tests;

public sealed class UmbracoClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Buffer the body so the test can inspect it after the call.
            if (request.Content is not null)
            {
                await request.Content.LoadIntoBufferAsync();
            }

            Requests.Add(request);
            return _responder(request);
        }
    }

    private static UmbracoClient NewClient(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://umbraco.local/") }, NullLogger<UmbracoClient>.Instance);

    [Fact]
    public async Task Login_extracts_only_the_name_value_part_of_each_set_cookie()
    {
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { username = "alice", email = "a@x.io", name = "alice" }),
            };
            response.Headers.TryAddWithoutValidation(
                "Set-Cookie", ".AspNetCore.Identity.Application=COOKIEVALUE; path=/; samesite=lax; httponly");
            response.Headers.TryAddWithoutValidation(
                "Set-Cookie", "UMB_UCONTEXT=ctx; path=/; httponly");
            return response;
        });
        UmbracoClient client = NewClient(handler);

        AuthResult result = await client.LoginAsync("alice", "pw");

        Assert.True(result.Ok);
        Assert.Equal("alice", result.Member!.Username);
        Assert.Equal(".AspNetCore.Identity.Application=COOKIEVALUE; UMB_UCONTEXT=ctx", result.Cookies);
    }

    [Fact]
    public async Task Login_without_set_cookie_is_treated_as_failure()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { username = "alice", email = "a@x.io", name = "alice" }),
        });

        AuthResult result = await NewClient(handler).LoginAsync("alice", "pw");

        Assert.False(result.Ok);
        Assert.Null(result.Cookies);
    }

    [Fact]
    public async Task Login_failure_surfaces_the_error_message_from_the_body()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = JsonContent.Create(new { error = "invalid username or password" }),
        });

        AuthResult result = await NewClient(handler).LoginAsync("alice", "bad");

        Assert.False(result.Ok);
        Assert.Equal("invalid username or password", result.Error);
    }

    [Fact]
    public async Task GetTranslations_forwards_the_stored_cookie_header()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                languages = new[] { "de-DE", "en-US" },
                count = 1,
                items = new[]
                {
                    new { key = "General.Save", path = "General / General.Save", group = "General", values = new Dictionary<string, string> { ["en-US"] = "Save" } },
                },
            }),
        });
        UmbracoClient client = NewClient(handler);

        TranslationsResponse response = await client.GetTranslationsAsync(".AspNetCore.Identity.Application=COOKIEVALUE");

        Assert.True(response.Ok);
        Assert.Equal(1, response.Data!.Count);
        Assert.Equal("General.Save", response.Data.Items[0].Key);

        HttpRequestMessage sent = Assert.Single(handler.Requests);
        Assert.True(sent.Headers.TryGetValues("Cookie", out IEnumerable<string>? cookie));
        Assert.Equal(".AspNetCore.Identity.Application=COOKIEVALUE", Assert.Single(cookie!));
    }

    [Fact]
    public async Task GetTranslations_maps_401_to_unauthorized()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        TranslationsResponse response = await NewClient(handler).GetTranslationsAsync("x=y");

        Assert.False(response.Ok);
        Assert.True(response.Unauthorized);
    }
}
