using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static PawHome.Api.Tests.MarketplaceData;
using Microsoft.Extensions.DependencyInjection;
using PawHome.Api.Data;
using PawHome.Api.Storage;

namespace PawHome.Api.Tests;

/// <summary>使用真实 Cookie 和数据库验证申请隐私、联系与照片权限。</summary>
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
        await app.SeedUserAsync("photopublisher");
        await app.LoginAsync(first, "photopublisher");
        await app.LoginAsync(second, "photopublisher");
        var animalId = await MarketplaceData.CreateAnimal(app, first);
        var bytes = Convert.FromBase64String(Image);
        var path = "/api/my/animals/" + animalId + "/photos";
        for (var index = 0; index < ExistingPhotos; index++) // 依次占用已有名额。
            Assert.Equal(HttpStatusCode.Created, (await app.SendAsync(first, HttpMethod.Post, path, Upload(bytes, "image/png"))).StatusCode);
        var attempts = await Task.WhenAll(
            app.SendAsync(first, HttpMethod.Post, path, Upload(bytes, "image/png")),
            app.SendAsync(second, HttpMethod.Post, path, Upload(bytes, "image/png")));
        Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.Conflict);
    }

    /// <summary>普通用户只能访问自己申请，不能查看他人申请或申请未上架动物。</summary>
    [Fact]
    public async Task ApplicantsCannotReadOthersOrSubmitDuplicateOrUnpublishedAnimals()
    {
        await using var app = new ApiFactory();
        using var owner = app.CreateCookieClient();
        using var alice = app.CreateCookieClient();
        using var bob = app.CreateCookieClient();
        using var guest = app.CreateCookieClient();
        await app.SeedUserAsync("owner");
        await app.SeedUserAsync("alice");
        await app.SeedUserAsync("bob");
        await app.LoginAsync(owner, "owner");
        await app.LoginAsync(alice, "alice");
        await app.LoginAsync(bob, "bob");
        var published = await MarketplaceData.CreateAnimal(app, owner);
        var hidden = await MarketplaceData.CreateAnimal(app, owner, false);
        var id = await Id(await app.PostAsync(alice, "/api/applications", Application(published)));
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync("/api/applications/" + id)).StatusCode);
        Assert.Equal("[]", await bob.GetStringAsync("/api/applications/mine"));
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync("/api/staff/applications")).StatusCode);
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
        using var outsider = app.CreateCookieClient();
        await app.SeedUserAsync("outsider");
        await app.LoginAsync(outsider, "outsider");
        await app.SeedUserAsync("publisher");
        await app.LoginAsync(staff, "publisher");
        var animalId = await MarketplaceData.CreateAnimal(app, staff, false);
        using var fake = Upload("<script>alert(1)</script>"u8.ToArray(), "image/png");
        Assert.Equal(HttpStatusCode.BadRequest, (await app.SendAsync(staff, HttpMethod.Post,
            "/api/my/animals/" + animalId + "/photos", fake)).StatusCode);
        // 使用完整的虚构 PNG 测试图片；不依赖网络或真实用户照片。
        var png = Convert.FromBase64String(Image);
        using var form = Upload(png, "image/png");
        var response = await app.SendAsync(staff, HttpMethod.Post, "/api/my/animals/" + animalId + "/photos", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var photoId = await Id(response);
        var photoUrl = "/api/animals/" + animalId + "/photos/" + photoId;
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync(photoUrl)).StatusCode);
        Assert.Equal(png, await staff.GetByteArrayAsync(photoUrl));
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(photoUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.SendAsync(outsider, HttpMethod.Post,
            "/api/my/animals/" + animalId + "/photos", Upload(png, "image/png"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.SendAsync(outsider, HttpMethod.Delete,
            "/api/my/animals/" + animalId + "/photos/" + photoId)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await app.PutAsync(staff, "/api/my/animals/" + animalId, Animal(true))).StatusCode);
        using var read = await guest.GetAsync(photoUrl);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal("image/png", read.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", read.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(HttpStatusCode.NoContent, (await app.SendAsync(staff, HttpMethod.Delete,
            "/api/my/animals/" + animalId + "/photos/" + photoId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync(photoUrl)).StatusCode);
    }

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
