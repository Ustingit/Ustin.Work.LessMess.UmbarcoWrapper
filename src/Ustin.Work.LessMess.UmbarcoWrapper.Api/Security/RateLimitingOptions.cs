namespace Ustin.Work.LessMess.UmbarcoWrapper.Api.Security;

/// <summary>Bound from the <c>RateLimiting</c> configuration section.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;

    /// <summary><c>/login</c>, <c>/password/reset</c>, <c>/email/confirm</c> — partitioned by client id + IP.</summary>
    public WindowPolicy Login { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    /// <summary><c>/token/refresh</c> — partitioned by client id + IP.</summary>
    public WindowPolicy Refresh { get; set; } = new() { PermitLimit = 30, WindowSeconds = 60 };

    /// <summary><c>/password/forgot</c>, <c>/email/resend</c> — partitioned by IP.</summary>
    public WindowPolicy PasswordForgot { get; set; } = new() { PermitLimit = 5, WindowSeconds = 900 };

    /// <summary><c>/register</c> — partitioned by IP.</summary>
    public WindowPolicy Register { get; set; } = new() { PermitLimit = 5, WindowSeconds = 3600 };

    /// <summary>Backstop concurrency limiter over every request — protects the DB / Umbraco from a flood.</summary>
    public int GlobalMaxConcurrent { get; set; } = 100;

    public int GlobalQueueLimit { get; set; } = 50;
}

public sealed class WindowPolicy
{
    public int PermitLimit { get; set; }

    public int WindowSeconds { get; set; }
}

public static class RateLimitPolicies
{
    public const string Login = "auth-login";
    public const string Refresh = "auth-refresh";
    public const string PasswordForgot = "auth-forgot";
    public const string Register = "auth-register";
}
