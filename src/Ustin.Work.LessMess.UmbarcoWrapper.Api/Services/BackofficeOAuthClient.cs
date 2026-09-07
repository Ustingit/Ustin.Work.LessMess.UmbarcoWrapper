using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Api.Services;

/// <summary>
///     A v2 session. Modern Umbraco (16.1+/17) runs the back office as a BFF: the
///     access/refresh tokens and the PKCE code are never returned to the client — they
///     live in encrypted HttpOnly cookies and the OAuth response only carries the
///     literal string <c>[redacted]</c>. So the wrapper cannot "hold a JWT"; it holds
///     the cookie set and replays it (plus <c>Authorization: Bearer [redacted]</c>).
/// </summary>
public sealed record BffSession(string Cookies, DateTimeOffset ExpiresAtUtc);

public sealed record BackofficeLoginResult(bool Ok, bool TwoFactorRequired, BffSession? Session, string? Error);

public sealed record BackofficeTranslationsResponse(bool Ok, bool Unauthorized, TranslationsResult? Data, string? Error);

public sealed class BackofficeOAuthClient
{
    private const string ClientId = "umbraco-back-office";
    private const string Redacted = "[redacted]";
    private const string LoginPath = "umbraco/management/api/v1/security/back-office/login";
    private const string AuthorizePath = "umbraco/management/api/v1/security/back-office/authorize";
    private const string TokenPath = "umbraco/management/api/v1/security/back-office/token";
    private const string RevokePath = "umbraco/management/api/v1/security/back-office/revoke";

    private readonly HttpClient _http;
    private readonly ILogger<BackofficeOAuthClient> _logger;
    private readonly string _redirectUri;

    public BackofficeOAuthClient(HttpClient http, WrapperOptions options, ILogger<BackofficeOAuthClient> logger)
    {
        _http = http;
        _logger = logger;
        _redirectUri = options.ManagementBaseUrl.TrimEnd('/') + "/umbraco/oauth_complete";
    }

    public async Task<BackofficeLoginResult> LoginAsync(string username, string password)
    {
        var jar = new Dictionary<string, string>();

        // 1. Interactive login -> back-office identity cookie (UMB_UCONTEXT).
        using HttpResponseMessage login = await _http.PostAsJsonAsync(LoginPath, new { username, password });
        if (login.StatusCode == HttpStatusCode.PaymentRequired)
        {
            return new BackofficeLoginResult(false, true, null, "two-factor authentication is enabled for this user");
        }

        if (!login.IsSuccessStatusCode)
        {
            return new BackofficeLoginResult(false, false, null, await DescribeAsync(login, "login failed"));
        }

        Merge(jar, login);

        // 2. Authorization Code + PKCE. The user is already authenticated (cookie),
        //    so /authorize redirects straight back — with code=[redacted] and a
        //    Set-Cookie carrying the real (encrypted) PKCE code.
        (var verifier, var challenge) = Pkce.Create();
        var authorizeUrl =
            $"{AuthorizePath}?client_id={ClientId}&response_type=code&redirect_uri={Uri.EscapeDataString(_redirectUri)}" +
            $"&scope={Uri.EscapeDataString("offline_access")}&code_challenge={challenge}&code_challenge_method=S256&state={Pkce.RandomState()}";

        using var authorizeRequest = new HttpRequestMessage(HttpMethod.Get, authorizeUrl);
        authorizeRequest.Headers.TryAddWithoutValidation("Cookie", Render(jar));
        using HttpResponseMessage authorize = await _http.SendAsync(authorizeRequest);

        if (authorize.StatusCode is not (HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.TemporaryRedirect))
        {
            return new BackofficeLoginResult(false, false, null,
                $"authorize did not redirect (HTTP {(int)authorize.StatusCode}); the login cookie may have been rejected");
        }

        Merge(jar, authorize);

        var location = authorize.Headers.Location?.ToString() ?? string.Empty;
        var queryStart = location.IndexOf('?');
        var code = queryStart >= 0 ? HttpUtility.ParseQueryString(location[(queryStart + 1)..])["code"] : null;
        _logger.LogDebug("v2: authorize -> {Status}, code param present: {HasCode}", (int)authorize.StatusCode, !string.IsNullOrEmpty(code));
        if (string.IsNullOrEmpty(code))
        {
            return new BackofficeLoginResult(false, false, null, $"no authorization code in redirect: {location}");
        }

        // 3. Exchange the (redacted) code + PKCE cookie for tokens. Umbraco swaps
        //    [redacted] for the real code, then redacts the tokens out of the
        //    response and drops them into HttpOnly cookies.
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, TokenPath)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = ClientId,
                ["code"] = code,
                ["redirect_uri"] = _redirectUri,
                ["code_verifier"] = verifier,
            }),
        };
        tokenRequest.Headers.TryAddWithoutValidation("Cookie", Render(jar));
        using HttpResponseMessage token = await _http.SendAsync(tokenRequest);

        if (!token.IsSuccessStatusCode)
        {
            return new BackofficeLoginResult(false, false, null, await DescribeAsync(token, "token exchange failed"));
        }

        Merge(jar, token);
        DateTimeOffset expiresAt = ReadExpiry(await token.Content.ReadFromJsonAsync<JsonElement>());

        _logger.LogInformation(
            "v2: back-office user {Username} authenticated (BFF cookie set: {Names}), access expires {Exp:o}",
            username, string.Join(",", jar.Keys), expiresAt);
        return new BackofficeLoginResult(true, false, new BffSession(Render(jar), expiresAt), null);
    }

    public async Task<BffSession?> RefreshAsync(string cookies)
    {
        Dictionary<string, string> jar = Parse(cookies);

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenPath)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = ClientId,
                ["refresh_token"] = Redacted,
            }),
        };
        request.Headers.TryAddWithoutValidation("Cookie", Render(jar));
        using HttpResponseMessage response = await _http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("v2: refresh failed ({Status})", (int)response.StatusCode);
            return null;
        }

        Merge(jar, response);
        return new BffSession(Render(jar), ReadExpiry(await response.Content.ReadFromJsonAsync<JsonElement>()));
    }

    public async Task LogoutAsync(string cookies)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, RevokePath)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = ClientId,
                    ["token"] = Redacted,
                    ["token_type_hint"] = "refresh_token",
                }),
            };
            request.Headers.TryAddWithoutValidation("Cookie", cookies);
            using HttpResponseMessage response = await _http.SendAsync(request);
            _logger.LogInformation("v2: token revocation responded {Status}", (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "v2: token revocation call failed");
        }
    }

    public async Task<BackofficeTranslationsResponse> GetTranslationsAsync(string cookies)
    {
        using HttpResponseMessage overviewResponse = await SendAsync(HttpMethod.Get,
            "umbraco/management/api/v1/dictionary?skip=0&take=1000", cookies);

        if (overviewResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new BackofficeTranslationsResponse(false, true, null, "umbraco rejected the back-office session");
        }

        if (!overviewResponse.IsSuccessStatusCode)
        {
            return new BackofficeTranslationsResponse(false, false, null, await DescribeAsync(overviewResponse, "dictionary list failed"));
        }

        JsonElement overview = await overviewResponse.Content.ReadFromJsonAsync<JsonElement>();
        DictionaryOverview[] rows = overview.GetProperty("items").EnumerateArray()
            .Select(e => new DictionaryOverview(
                e.GetProperty("id").GetGuid(),
                e.GetProperty("name").GetString() ?? string.Empty,
                e.TryGetProperty("parent", out JsonElement p) && p.ValueKind == JsonValueKind.Object
                    ? p.GetProperty("id").GetGuid()
                    : null))
            .Where(r => r.Name != "Wrapper.Seeded") // internal seeder marker, hidden like in v1
            .ToArray();

        var nameById = rows.ToDictionary(r => r.Id, r => r.Name);

        string[] languages;
        using (HttpResponseMessage langResponse = await SendAsync(HttpMethod.Get,
                   "umbraco/management/api/v1/language?skip=0&take=100", cookies))
        {
            languages = langResponse.IsSuccessStatusCode
                ? (await langResponse.Content.ReadFromJsonAsync<JsonElement>())
                    .GetProperty("items").EnumerateArray()
                    .Select(e => e.GetProperty("isoCode").GetString() ?? string.Empty)
                    .Where(c => c.Length > 0).OrderBy(c => c).ToArray()
                : Array.Empty<string>();
        }

        var items = new List<TranslationItem>(rows.Length);
        foreach (DictionaryOverview row in rows.OrderBy(r => Path(r, nameById, rows)))
        {
            var values = new Dictionary<string, string>();
            using (HttpResponseMessage detail = await SendAsync(HttpMethod.Get,
                       $"umbraco/management/api/v1/dictionary/{row.Id}", cookies))
            {
                if (detail.IsSuccessStatusCode)
                {
                    foreach (JsonElement t in (await detail.Content.ReadFromJsonAsync<JsonElement>())
                                 .GetProperty("translations").EnumerateArray())
                    {
                        var iso = t.GetProperty("isoCode").GetString();
                        var value = t.GetProperty("translation").GetString();
                        if (!string.IsNullOrEmpty(iso) && !string.IsNullOrEmpty(value))
                        {
                            values[iso] = value;
                        }
                    }
                }
            }

            var group = row.ParentId is { } pid && nameById.ContainsKey(pid)
                ? ParentPath(pid, nameById, rows)
                : null;
            items.Add(new TranslationItem(row.Name, Path(row, nameById, rows), group, values));
        }

        _logger.LogInformation("v2: management api returned {Count} dictionary items via the BFF session", items.Count);
        return new BackofficeTranslationsResponse(true, false, new TranslationsResult(languages, items.Count, items), null);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string cookies)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Redacted);
        request.Headers.TryAddWithoutValidation("Cookie", cookies);
        return _http.SendAsync(request);
    }

    private sealed record DictionaryOverview(Guid Id, string Name, Guid? ParentId);

    private static string Path(DictionaryOverview row, Dictionary<Guid, string> nameById, DictionaryOverview[] all)
        => row.ParentId is { } pid && nameById.ContainsKey(pid)
            ? ParentPath(pid, nameById, all) + " / " + row.Name
            : row.Name;

    private static string ParentPath(Guid id, Dictionary<Guid, string> nameById, DictionaryOverview[] all)
    {
        var parts = new List<string>();
        Guid? current = id;
        while (current is { } c && nameById.TryGetValue(c, out var name))
        {
            parts.Insert(0, name);
            current = all.FirstOrDefault(r => r.Id == c)?.ParentId;
        }

        return string.Join(" / ", parts);
    }

    private static DateTimeOffset ReadExpiry(JsonElement body) =>
        DateTimeOffset.UtcNow.AddSeconds(
            body.TryGetProperty("expires_in", out JsonElement ei) && ei.TryGetInt32(out var s) ? s : 3600);

    private static void Merge(Dictionary<string, string> jar, HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? setCookies))
        {
            return;
        }

        foreach (var raw in setCookies)
        {
            var pair = raw.Split(';', 2)[0].Trim();
            var eq = pair.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var name = pair[..eq];
            var value = pair[(eq + 1)..];
            if (value.Length == 0 || value == "deleted")
            {
                jar.Remove(name);
            }
            else
            {
                jar[name] = value;
            }
        }
    }

    private static Dictionary<string, string> Parse(string cookies)
    {
        var jar = new Dictionary<string, string>();
        foreach (var part in cookies.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq > 0)
            {
                jar[part[..eq]] = part[(eq + 1)..];
            }
        }

        return jar;
    }

    private static string Render(Dictionary<string, string> jar) =>
        string.Join("; ", jar.Select(kv => $"{kv.Key}={kv.Value}"));

    private static async Task<string> DescribeAsync(HttpResponseMessage response, string fallback)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync();
            if (text.Length is > 0 and < 400)
            {
                return $"{fallback}: {(int)response.StatusCode} {text}";
            }
        }
        catch (Exception)
        {
            // ignore
        }

        return $"{fallback}: HTTP {(int)response.StatusCode}";
    }
}
