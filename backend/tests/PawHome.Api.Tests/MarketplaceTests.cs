using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
namespace PawHome.Api.Tests;
/// <summary>验证同一账号的双向使用、归属隐私与三只上架名额。</summary>
public sealed class MarketplaceTests
{
    private const int PublishLimit = 3; // 每个账号最多同时上架三只。
    /// <summary>手机号必填而微信号可省略，两者分别按申请合同验证。</summary>
    [Theory]
    [InlineData(MarketplaceData.Phone, null, HttpStatusCode.Created)]
    [InlineData(MarketplaceData.Phone, MarketplaceData.WeChat, HttpStatusCode.Created)]
    [InlineData("", MarketplaceData.WeChat, HttpStatusCode.BadRequest)]
    public async Task PhoneIsRequiredAndWeChatIsOptional(string phone, string? weChat, HttpStatusCode expected)
    {
        await using var app = new ApiFactory();
        using var publisher = app.CreateCookieClient();
        using var applicant = app.CreateCookieClient();
        await app.SeedUserAsync("contactpublisher"); await app.SeedUserAsync("contactapplicant");
        await app.LoginAsync(publisher, "contactpublisher"); await app.LoginAsync(applicant, "contactapplicant");
        var animalId = await MarketplaceData.CreateAnimal(app, publisher);
        var response = await app.PostAsync(applicant, "/api/applications", new
        {
            animalId, name = "虚构申请人", phone, weChat, residence = "虚构住所",
            petExperience = "测试经验", reason = "测试理由"
        });
        Assert.Equal(expected, response.StatusCode);
    }
    /// <summary>同一账号可以发布自己的动物，也可申请别人的动物；联系方式按动物归属隔离。</summary>
    [Fact]
    public async Task AccountsCanPublishAndApplyButOnlyPublisherReceivesContacts()
    {
        await using var app = new ApiFactory();
        using var alice = app.CreateCookieClient();
        using var bob = app.CreateCookieClient();
        using var outsider = app.CreateCookieClient();
        using var guest = app.CreateCookieClient();
        await app.SeedUserAsync("alice");
        await app.SeedUserAsync("bob");
        await app.SeedUserAsync("outsider");
        await app.LoginAsync(alice, "alice"); await app.LoginAsync(bob, "bob");
        await app.LoginAsync(outsider, "outsider");
        var aliceAnimal = await MarketplaceData.CreateAnimal(app, alice);
        var bobAnimal = await MarketplaceData.CreateAnimal(app, bob);
        var response = await app.PostAsync(bob, "/api/applications", MarketplaceData.Application(aliceAnimal));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var applicationId = await MarketplaceData.Id(response);
        Assert.Equal(HttpStatusCode.Created, (await app.PostAsync(alice, "/api/applications", MarketplaceData.Application(bobAnimal))).StatusCode);
        var incoming = await alice.GetStringAsync($"/api/my/animals/{aliceAnimal}/applications");
        Assert.Contains(MarketplaceData.Phone, incoming);
        Assert.Contains(MarketplaceData.WeChat, incoming);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/my/animals/{aliceAnimal}/applications")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/my/animals/{aliceAnimal}/applications")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync("/api/applications/" + applicationId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync("/api/applications/" + applicationId)).StatusCode);
        Assert.Contains(MarketplaceData.WeChat, await bob.GetStringAsync("/api/applications/mine"));
        var publicAnimals = await guest.GetStringAsync("/api/animals");
        Assert.DoesNotContain(MarketplaceData.Phone, publicAnimals);
        Assert.DoesNotContain(MarketplaceData.WeChat, publicAnimals);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.PostAsync(alice, "/api/applications", MarketplaceData.Application(aliceAnimal))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.PutAsync(outsider, "/api/my/animals/" + aliceAnimal, MarketplaceData.Animal(false))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.PutAsync(outsider, $"/api/my/applications/{applicationId}/decision",
            new { status = "Approved" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await app.PutAsync(alice, $"/api/my/applications/{applicationId}/decision",
            new { status = "Approved", note = "已私下沟通" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await app.PutAsync(alice, $"/api/my/applications/{applicationId}/decision",
            new { status = "Rejected" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync("/api/staff/applications")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync("/api/staff/volunteers")).StatusCode);
    }

    /// <summary>草稿不占名额，编辑已发布档案不占新名额，下架后可发布另一只。</summary>
    [Fact]
    public async Task ThreeActiveListingsAllowDraftsAndUnlistingReleasesSlot()
    {
        await using var app = new ApiFactory();
        using var publisher = app.CreateCookieClient();
        using var other = app.CreateCookieClient();
        await app.SeedUserAsync("publisher"); await app.SeedUserAsync("other");
        await app.LoginAsync(publisher, "publisher"); await app.LoginAsync(other, "other");
        var active = new List<long>();
        for (var index = 0; index < PublishLimit; index++)
            active.Add(await MarketplaceData.CreateAnimal(app, publisher));
        Assert.Equal(HttpStatusCode.Conflict, (await app.PostAsync(publisher, "/api/my/animals", MarketplaceData.Animal(true))).StatusCode);
        var draft = await MarketplaceData.CreateAnimal(app, publisher, false);
        Assert.Equal(HttpStatusCode.NoContent, (await app.PutAsync(publisher, "/api/my/animals/" + active.First(), MarketplaceData.Animal(true))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await app.PutAsync(publisher, "/api/my/animals/" + draft, MarketplaceData.Animal(true))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await app.PutAsync(publisher, "/api/my/animals/" + active.First(), MarketplaceData.Animal(false))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await app.PutAsync(publisher, "/api/my/animals/" + draft, MarketplaceData.Animal(true))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await app.PostAsync(other, "/api/my/animals", MarketplaceData.Animal(true))).StatusCode);
        var mine = await publisher.GetFromJsonAsync<JsonElement>("/api/my/animals");
        Assert.Equal(PublishLimit, mine.EnumerateArray().Count(animal => animal.GetProperty("isPublished").GetBoolean()));
        Assert.DoesNotContain("other", mine.GetRawText());
    }

    /// <summary>同一账号两个并发发布请求只能使用最后一个名额。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ConcurrentPublishingCannotExceedThree(bool firstUpdatesDraft, bool secondUpdatesDraft)
    {
        const int ExistingListings = PublishLimit - 1; // 上架名额只剩一个。
        await using var app = new ApiFactory();
        using var first = app.CreateCookieClient();
        using var second = app.CreateCookieClient();
        await app.SeedUserAsync("concurrent");
        await app.LoginAsync(first, "concurrent"); await app.LoginAsync(second, "concurrent");
        for (var index = 0; index < ExistingListings; index++) await MarketplaceData.CreateAnimal(app, first);
        var firstDraft = await MarketplaceData.CreateAnimal(app, first, false);
        var secondDraft = await MarketplaceData.CreateAnimal(app, first, false);
        var attempts = await Task.WhenAll(
            firstUpdatesDraft
                ? app.PutAsync(first, "/api/my/animals/" + firstDraft, MarketplaceData.Animal(true))
                : app.PostAsync(first, "/api/my/animals", MarketplaceData.Animal(true)),
            secondUpdatesDraft
                ? app.PutAsync(second, "/api/my/animals/" + secondDraft, MarketplaceData.Animal(true))
                : app.PostAsync(second, "/api/my/animals", MarketplaceData.Animal(true)));
        Assert.Single(attempts, response => response.StatusCode is HttpStatusCode.Created or HttpStatusCode.NoContent);
        Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.Conflict);
        var mine = await first.GetFromJsonAsync<JsonElement>("/api/my/animals");
        Assert.Equal(PublishLimit, mine.EnumerateArray().Count(animal => animal.GetProperty("isPublished").GetBoolean()));
    }
}
/// <summary>所有领域测试复用虚构动物、申请字段与创建请求。</summary>
public static class MarketplaceData
{
    public const string Phone = "13800000000"; // 虚构联系电话。
    public const string WeChat = "test_wechat"; // 虚构微信号。
    public const string Image = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l1sAAAAASUVORK5CYII="; // 单像素测试 PNG。
    /// <summary>构造公开或草稿动物，不包含客户端指定的归属字段。</summary>
    public static object Animal(bool published) => new
    {
        name = "测试小橘", species = "猫", sex = "母", ageMonths = 6, // 虚构六个月动物。
        city = "测试市", description = "虚构测试动物", isPublished = published
    };
    /// <summary>构造不包含身份证号的申请，联系方式仅用虚构内容。</summary>
    public static object Application(long animalId) => new
    {
        animalId, name = "测试申请人", phone = Phone, weChat = WeChat, residence = "测试住所",
        petExperience = "测试养宠经验", reason = "测试领养理由"
    };
    /// <summary>调用真实发布接口并读取新动物 ID。</summary>
    public static async Task<long> CreateAnimal(ApiFactory app, HttpClient publisher, bool published = true)
    {
        var response = await app.PostAsync(publisher, "/api/my/animals", Animal(published));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Id(response);
    }
    /// <summary>读取资源创建响应中的 ID。</summary>
    public static async Task<long> Id(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
}
