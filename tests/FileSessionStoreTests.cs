using Ustin.Work.LessMess.UmbarcoWrapper.Web.Services;
using Xunit;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Web.Tests;

public sealed class FileSessionStoreTests
{
    private static WrapperOptions Options(TempDir dir) => new() { DataDirectory = dir.Path };

    [Fact]
    public async Task Create_then_Get_round_trips_the_record()
    {
        using var dir = new TempDir();
        var store = new FileSessionStore(Options(dir));

        var id = await store.CreateAsync("alice", "alice@example.com", ".auth=abc; extra=1", "10.0.0.5");
        SessionRecord? record = await store.GetAsync(id);

        Assert.NotNull(record);
        Assert.Equal("alice", record!.Username);
        Assert.Equal("alice@example.com", record.Email);
        Assert.Equal(".auth=abc; extra=1", record.Cookies);
        Assert.Equal("10.0.0.5", record.Ip);
    }

    [Fact]
    public async Task Session_survives_a_new_store_instance_same_directory()
    {
        using var dir = new TempDir();
        var id = await new FileSessionStore(Options(dir)).CreateAsync("bob", "bob@example.com", ".auth=xyz", null);

        SessionRecord? record = await new FileSessionStore(Options(dir)).GetAsync(id);

        Assert.NotNull(record);
        Assert.Equal("bob", record!.Username);
    }

    [Fact]
    public async Task Touch_updates_LastSeen_and_optionally_cookies()
    {
        using var dir = new TempDir();
        var store = new FileSessionStore(Options(dir));
        var id = await store.CreateAsync("carol", "c@example.com", ".auth=old", null);
        SessionRecord created = (await store.GetAsync(id))!;

        await Task.Delay(10);
        await store.TouchAsync(id, ".auth=new");

        SessionRecord touched = (await store.GetAsync(id))!;
        Assert.Equal(".auth=new", touched.Cookies);
        Assert.True(touched.LastSeenUtc >= created.LastSeenUtc);
        Assert.Equal(created.CreatedUtc, touched.CreatedUtc);
    }

    [Fact]
    public async Task Touch_without_cookies_keeps_the_existing_cookies()
    {
        using var dir = new TempDir();
        var store = new FileSessionStore(Options(dir));
        var id = await store.CreateAsync("dave", "d@example.com", ".auth=keep", null);

        await store.TouchAsync(id);

        Assert.Equal(".auth=keep", (await store.GetAsync(id))!.Cookies);
    }

    [Fact]
    public async Task Remove_deletes_the_session()
    {
        using var dir = new TempDir();
        var store = new FileSessionStore(Options(dir));
        var id = await store.CreateAsync("erin", "e@example.com", ".auth=1", null);

        await store.RemoveAsync(id);

        Assert.Null(await store.GetAsync(id));
    }

    [Fact]
    public async Task Get_returns_null_for_an_unknown_id()
    {
        using var dir = new TempDir();
        var store = new FileSessionStore(Options(dir));

        Assert.Null(await store.GetAsync("nope"));
    }
}
