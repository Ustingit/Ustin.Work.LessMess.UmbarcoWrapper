using Ustin.Work.LessMess.UmbarcoWrapper.Api.Services;
using Xunit;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Tests;

public sealed class InMemorySessionStoreTests
{
    [Fact]
    public async Task Create_then_Get_round_trips_the_record()
    {
        var store = new InMemorySessionStore();

        var id = await store.CreateAsync("alice", "alice@example.com", ".auth=abc; extra=1", "10.0.0.5");
        SessionRecord? record = await store.GetAsync(id);

        Assert.NotNull(record);
        Assert.Equal("alice", record!.Username);
        Assert.Equal("alice@example.com", record.Email);
        Assert.Equal(".auth=abc; extra=1", record.Cookies);
        Assert.Equal("10.0.0.5", record.Ip);
    }

    [Fact]
    public async Task Touch_updates_LastSeen_and_optionally_cookies()
    {
        var store = new InMemorySessionStore();
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
        var store = new InMemorySessionStore();
        var id = await store.CreateAsync("dave", "d@example.com", ".auth=keep", null);

        await store.TouchAsync(id);

        Assert.Equal(".auth=keep", (await store.GetAsync(id))!.Cookies);
    }

    [Fact]
    public async Task Touch_on_an_unknown_id_is_a_no_op()
    {
        var store = new InMemorySessionStore();

        await store.TouchAsync("nope", ".auth=x");

        Assert.Null(await store.GetAsync("nope"));
    }

    [Fact]
    public async Task Remove_deletes_the_session()
    {
        var store = new InMemorySessionStore();
        var id = await store.CreateAsync("erin", "e@example.com", ".auth=1", null);

        await store.RemoveAsync(id);

        Assert.Null(await store.GetAsync(id));
    }

    [Fact]
    public async Task Get_returns_null_for_an_unknown_id()
    {
        Assert.Null(await new InMemorySessionStore().GetAsync("nope"));
    }
}
