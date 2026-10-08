using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PawHome.Api.Data;
using PawHome.Api.Storage;

namespace PawHome.Api.Tests;

/// <summary>使用真实 Cookie 和数据库验证申请隐私、审核与照片权限。</summary>
public sealed class DomainTests
{
    /// <summary>取消保存时不能残留没有元数据的图片文件。</summary>
    [Fact]
    public async Task CancelledPhotoSaveRemovesPartialFile()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateCookieClient();
        var storage = app.Services.GetRequiredService<IPhotoStorage>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var source = new MemoryStream("test image bytes"u8.ToArray());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.SaveAsync(source, ".png", cancellation.Token));
        Assert.Empty(Directory.GetFiles(Path.Combine(app.PrivateRoot, "photos")));
    }

    /// <summary>最后一个照片名额只能被一个并发上传占用。</summary>
    [Fact]
    public async Task ConcurrentUploadsCannotExceedPhotoLimit()
    {
        const int ExistingPhotos = 9; // 十张上限只剩一个名额。
        await using var app = new ApiFactory();
        using var first = app.CreateCookieClient();
        using var second = app.CreateCookieClient();
        await app.SeedUserAsync("photostaff", Roles.Volunteer);
        await app.LoginAsync(first, "photostaff");
        await app.LoginAsync(second, "photostaff");
        var animalId = await CreateAnimal(app, first);
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l1sAAAAASUVORK5CYII=");
        var path = "/api/staff/animals/" + animalId + "/photos";
        for (var index = 0; index < ExistingPhotos; index++) // 依次占用已有名额。
            Assert.Equal(HttpStatusCode.Created, (await app.SendAsync(first, HttpMethod.Post, path, Upload(bytes, "image/png"))).StatusCode);
        var attempts = await Task.WhenAll(
            app.SendAsync(first, HttpMethod.Post, path, Upload(bytes, "image/png")),
            app.SendAsync(second, HttpMethod.Post, path, Upload(bytes, "image/png")));
        Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.Conflict);
    }

    /// <summary>负责人和主负责人权限相同；志愿者只能管理动物。</summary>
    [Theory]
    [InlineData(Roles.Owner)]
    [InlineData(Roles.Manager)]
    public async Task ManagersCanDecideButVolunteerCannotReadApplications(string managerRole)
    {
        await using var app = new ApiFactory();
        using var manager = app.CreateCookieClient();
        using var volunteer = app.CreateCookieClient();
        using var adopter = app.CreateCookieClient();
        await app.SeedUserAsync("manager", managerRole);
        await app.SeedUserAsync("volunteer", Roles.Volunteer);
        await app.SeedUserAsync("adopter");
        await app.LoginAsync(manager, "manager");
        await app.LoginAsync(volunteer, "volunteer");
        await app.LoginAsync(adopter, "adopter");
        var animalId = await CreateAnimal(app, volunteer);
        var submit = await app.PostAsync(adopter, "/api/applications", Application(animalId));
        Assert.Equal(HttpStatusCode.Created, submit.StatusCode);
        var id = await Id(submit);
        Assert.Equal(HttpStatusCode.Forbidden, (await volunteer.GetAsync("/api/staff/applications")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await volunteer.GetAsync("/api/applications/mine")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await volunteer.GetAsync("/api/applications/" + id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.PutAsync(volunteer, "/api/staff/applications/" + id + "/decision",
            new { status = "Approved", note = "越权审核" })).StatusCode);
        var list = await manager.GetStringAsync("/api/staff/applications");
        Assert.Contains("13800000000", list); // 虚构号码，负责人需要查看申请联系方式。
        Assert.Equal(HttpStatusCode.NoContent, (await app.PutAsync(manager, "/api/staff/applications/" + id + "/decision",
            new { status = "Approved", note = "测试通过" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await app.PutAsync(manager, "/api/staff/applications/" + id + "/decision",
            new { status = "Rejected", note = "不能覆盖" })).StatusCode);
        var mine = await adopter.GetStringAsync("/api/applications/mine");
        Assert.Contains("Approved", mine);
    }

    /// <summary>普通用户只能访问自己申请，不能查看后台或修改动物。</summary>
    [Fact]
    public async Task ApplicantsCannotReadOthersOrSubmitDuplicateOrUnpublishedAnimals()
    {
        await using var app = new ApiFactory();
        using var owner = app.CreateCookieClient();
        using var alice = app.CreateCookieClient();
        using var bob = app.CreateCookieClient();
        using var guest = app.CreateCookieClient();
        await app.SeedUserAsync("owner", Roles.Owner);
        await app.SeedUserAsync("alice");
        await app.SeedUserAsync("bob");
        await app.LoginAsync(owner, "owner");
        await app.LoginAsync(alice, "alice");
        await app.LoginAsync(bob, "bob");
        var published = await CreateAnimal(app, owner);
        var hidden = await CreateAnimal(app, owner, false);
        var id = await Id(await app.PostAsync(alice, "/api/applications", Application(published)));
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync("/api/applications/" + id)).StatusCode);
        Assert.Equal("[]", await bob.GetStringAsync("/api/applications/mine"));
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync("/api/staff/applications")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.PostAsync(alice, "/api/staff/animals", Animal(true))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await app.PostAsync(alice, "/api/applications", Application(published))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.PostAsync(alice, "/api/applications", Application(hidden))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync("/api/animals/" + hidden)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.PostAsync(guest, "/api/applications", Application(published))).StatusCode);
        var publicJson = await guest.GetStringAsync("/api/animals");
        Assert.DoesNotContain("13800000000", publicJson);
        Assert.DoesNotContain("applicant", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("身份证", publicJson);
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.GetAsync("/api/animals?pageSize=101")).StatusCode); // 超过配置的单页最大一百条。
    }

    /// <summary>测试二进制签名、非公开照片读取和照片删除。</summary>
    [Fact]
    public async Task PhotosRejectDisguisedFilesAndFollowPublishVisibility()
    {
        await using var app = new ApiFactory();
        using var staff = app.CreateCookieClient();
        using var guest = app.CreateCookieClient();
        await app.SeedUserAsync("volunteer", Roles.Volunteer);
        await app.LoginAsync(staff, "volunteer");
        var animalId = await CreateAnimal(app, staff, false);
        using var fake = Upload("<script>alert(1)</script>"u8.ToArray(), "image/png");
        Assert.Equal(HttpStatusCode.BadRequest, (await app.SendAsync(staff, HttpMethod.Post,
            "/api/staff/animals/" + animalId + "/photos", fake)).StatusCode);
        // 使用完整的虚构 PNG 测试图片；不依赖网络或真实用户照片。
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l1sAAAAASUVORK5CYII=");
        using var form = Upload(png, "image/png");
        var response = await app.SendAsync(staff, HttpMethod.Post, "/api/staff/animals/" + animalId + "/photos", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var photoId = await Id(response);
        var photoUrl = "/api/animals/" + animalId + "/photos/" + photoId;
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync(photoUrl)).StatusCode);
        Assert.Equal(png, await staff.GetByteArrayAsync(photoUrl));
        Assert.Equal(HttpStatusCode.NoContent, (await app.PutAsync(staff, "/api/staff/animals/" + animalId, Animal(true))).StatusCode);
        using var read = await guest.GetAsync(photoUrl);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal("image/png", read.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", read.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(HttpStatusCode.NoContent, (await app.SendAsync(staff, HttpMethod.Delete,
            "/api/staff/animals/" + animalId + "/photos/" + photoId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync(photoUrl)).StatusCode);
    }

    /// <summary>统一测试动物字段，避免测试间重复构造不一致请求。</summary>
    private static object Animal(bool published) => new
    {
        name = "测试小橘", species = "猫", sex = "母", ageMonths = 6, // 六个月的虚构动物。
        city = "测试市", description = "虚构动物，仅用于测试。", isPublished = published
    };
    /// <summary>创建动物并检查真实授权端点响应。</summary>
    private static async Task<long> CreateAnimal(ApiFactory app, HttpClient staff, bool published = true)
    {
        var response = await app.PostAsync(staff, "/api/staff/animals", Animal(published));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Id(response);
    }
    /// <summary>构造无真实个人信息的申请。</summary>
    private static object Application(long animalId) => new
    {
        animalId, name = "测试申请人", phone = "13800000000", residence = "测试住所",
        petExperience = "测试养宠经验", reason = "测试领养理由"
    };
    /// <summary>读取创建响应中的资源 ID。</summary>
    private static async Task<long> Id(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
    /// <summary>创建 multipart 请求，并故意提供不可信文件名验证服务器生成对象键。</summary>
    private static MultipartFormDataContent Upload(byte[] bytes, string contentType)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(content, "file", "../../not-trusted.png");
        return form;
    }
}
