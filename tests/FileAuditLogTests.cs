using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Ustin.Work.LessMess.UmbarcoWrapper.Web.Services;
using Xunit;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Web.Tests;

public sealed class FileAuditLogTests
{
    private static FileAuditLog NewLog(TempDir dir) =>
        new(new WrapperOptions { DataDirectory = dir.Path }, NullLogger<FileAuditLog>.Instance);

    [Fact]
    public async Task Writes_one_json_object_per_line_with_the_expected_fields()
    {
        using var dir = new TempDir();
        var log = NewLog(dir);

        await log.WriteAsync(new AuditEntry("login", "alice", "10.0.0.5", "curl/8", "success", "cookie stored")
        {
            Timestamp = DateTimeOffset.Parse("2026-09-02T18:20:01.512Z"),
        });

        var line = Assert.Single(File.ReadAllLines(Path.Combine(dir.Path, "audit.log")));
        using JsonDocument doc = JsonDocument.Parse(line);
        JsonElement root = doc.RootElement;

        Assert.Equal("2026-09-02T18:20:01.5120000+00:00", root.GetProperty("ts").GetString());
        Assert.Equal("login", root.GetProperty("event").GetString());
        Assert.Equal("alice", root.GetProperty("username").GetString());
        Assert.Equal("10.0.0.5", root.GetProperty("ip").GetString());
        Assert.Equal("curl/8", root.GetProperty("userAgent").GetString());
        Assert.Equal("success", root.GetProperty("outcome").GetString());
        Assert.Equal("cookie stored", root.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Appends_rather_than_overwrites()
    {
        using var dir = new TempDir();
        var log = NewLog(dir);

        await log.WriteAsync(new AuditEntry("register", "bob", null, null, "success"));
        await log.WriteAsync(new AuditEntry("login", "bob", null, null, "success"));
        await log.WriteAsync(new AuditEntry("logout", "bob", null, null, "success"));

        string[] lines = File.ReadAllLines(Path.Combine(dir.Path, "audit.log"));
        Assert.Equal(3, lines.Length);
        Assert.Collection(
            lines,
            l => Assert.Equal("register", JsonDocument.Parse(l).RootElement.GetProperty("event").GetString()),
            l => Assert.Equal("login", JsonDocument.Parse(l).RootElement.GetProperty("event").GetString()),
            l => Assert.Equal("logout", JsonDocument.Parse(l).RootElement.GetProperty("event").GetString()));
    }

    [Fact]
    public async Task Null_ip_and_detail_are_written_as_json_null()
    {
        using var dir = new TempDir();
        var log = NewLog(dir);

        await log.WriteAsync(new AuditEntry("login.failed", "eve", null, null, "failure"));

        var line = File.ReadAllText(Path.Combine(dir.Path, "audit.log")).Trim();
        using JsonDocument doc = JsonDocument.Parse(line);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("ip").ValueKind);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("detail").ValueKind);
    }
}
