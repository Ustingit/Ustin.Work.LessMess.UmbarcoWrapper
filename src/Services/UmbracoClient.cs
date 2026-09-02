using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Web.Services;

public sealed record UmbracoMember(string Username, string Email, string Name);

public sealed record TranslationItem(string Key, string Path, string? Group, Dictionary<string, string> Values);

public sealed record TranslationsResult(string[] Languages, int Count, List<TranslationItem> Items);

public sealed record AuthResult(bool Ok, UmbracoMember? Member, string? Cookies, string? Error);

public sealed record TranslationsResponse(bool Ok, bool Unauthorized, TranslationsResult? Data, string? Error);

/// <summary>
///     Talks to the custom <c>/api/wrapper/*</c> endpoints on the Umbraco instance.
///     Login responses carry the member auth cookie via <c>Set-Cookie</c>; this client
///     hands the raw <c>name=value</c> pairs back to the caller to persist and replay.
/// </summary>
public sealed class UmbracoClient
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly ILogger<UmbracoClient> _logger;

    public UmbracoClient(HttpClient http, ILogger<UmbracoClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<AuthResult> RegisterAsync(string username, string email, string password)
    {
        using HttpResponseMessage response = await _http.PostAsJsonAsync(
            "api/wrapper/auth/register",
            new { username, email, password });

        if (response.IsSuccessStatusCode)
        {
            UmbracoMember? member = await response.Content.ReadFromJsonAsync<UmbracoMember>(Json);
            return new AuthResult(true, member, null, null);
        }

        return new AuthResult(false, null, null, await ReadErrorAsync(response));
    }

    public async Task<AuthResult> LoginAsync(string username, string password)
    {
        using HttpResponseMessage response = await _http.PostAsJsonAsync(
            "api/wrapper/auth/login",
            new { username, password });

        if (!response.IsSuccessStatusCode)
        {
            return new AuthResult(false, null, null, await ReadErrorAsync(response));
        }

        UmbracoMember? member = await response.Content.ReadFromJsonAsync<UmbracoMember>(Json);
        var cookies = ExtractCookies(response);
        if (string.IsNullOrEmpty(cookies))
        {
            _logger.LogWarning("umbraco login for {Username} returned no Set-Cookie header", username);
            return new AuthResult(false, null, null, "umbraco did not issue an auth cookie");
        }

        return new AuthResult(true, member, cookies, null);
    }

    public async Task LogoutAsync(string cookies)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/wrapper/auth/logout");
        request.Headers.TryAddWithoutValidation("Cookie", cookies);
        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request);
            _logger.LogInformation("umbraco logout responded {Status}", (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            // Best effort: the wrapper session is dropped regardless.
            _logger.LogWarning(ex, "umbraco logout call failed");
        }
    }

    public async Task<TranslationsResponse> GetTranslationsAsync(string cookies)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/wrapper/translations");
        request.Headers.TryAddWithoutValidation("Cookie", cookies);

        using HttpResponseMessage response = await _http.SendAsync(request);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new TranslationsResponse(false, true, null, "umbraco session expired");
        }

        if (!response.IsSuccessStatusCode)
        {
            return new TranslationsResponse(false, false, null, await ReadErrorAsync(response));
        }

        TranslationsResult? data = await response.Content.ReadFromJsonAsync<TranslationsResult>(Json);
        return new TranslationsResponse(true, false, data, null);
    }

    private static string ExtractCookies(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? setCookies))
        {
            return string.Empty;
        }

        // Keep only the "name=value" part of each Set-Cookie; drop attributes (path, samesite, ...).
        IEnumerable<string> pairs = setCookies
            .Select(c => c.Split(';', 2)[0].Trim())
            .Where(p => p.Contains('=', StringComparison.Ordinal));

        return string.Join("; ", pairs);
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("error", out JsonElement error))
            {
                return error.GetString() ?? response.ReasonPhrase ?? "request failed";
            }
        }
        catch (JsonException)
        {
            // fall through
        }

        return $"{(int)response.StatusCode} {response.ReasonPhrase}";
    }
}
