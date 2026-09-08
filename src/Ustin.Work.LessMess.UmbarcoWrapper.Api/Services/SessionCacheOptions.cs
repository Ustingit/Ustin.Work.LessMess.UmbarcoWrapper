namespace Ustin.Work.LessMess.UmbarcoWrapper.Api.Services;

/// <summary>Bound from the <c>Sessions</c> configuration section. Shared by both session stores.</summary>
public sealed class SessionCacheOptions
{
    public const string SectionName = "Sessions";

    /// <summary>Slides on every access; an idle session is evicted after this.</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Hard cap from creation, regardless of activity.</summary>
    public TimeSpan MaxLifetime { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Safety bound on concurrent sessions per store (LRU-ish eviction past this).</summary>
    public long MaxEntries { get; set; } = 50_000;
}
