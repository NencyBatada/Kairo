using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.Modules.BrandKit.Files;

namespace Kairo.IntegrationTests.BrandKit;

public sealed class BrandKitFileTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_uploaded_guideline_is_listed_and_downloads_as_it_was_sent()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await admin.Browser.PostAsync(Files(brandId), BrandKits.File(BrandKits.Pdf, "brand-guide.pdf"), Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var file = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal("guideline", file.GetProperty("kind").GetString());
        Assert.Equal("brand-guide.pdf", file.GetProperty("fileName").GetString());
        Assert.Equal("application/pdf", file.GetProperty("contentType").GetString());
        Assert.Equal(BrandKits.Pdf.Length, file.GetProperty("size").GetInt64());
        Assert.False(file.GetProperty("deleted").GetBoolean());
        var id = file.GetProperty("id").GetGuid();
        var listed = await admin.Browser.GetFromJsonAsync<JsonElement>(Files(brandId), Token);
        Assert.Equal(id, Assert.Single(listed.EnumerateArray()).GetProperty("id").GetGuid());
        var download = await admin.Browser.GetAsync($"{Files(brandId)}/{id}/download", Token);
        Assert.Equal(BrandKits.Pdf, await download.Content.ReadAsByteArrayAsync(Token));
        Assert.Contains("brand_kit.file_uploaded", await Workspaces.AuditActionsAsync(admin.Browser, brandId));
    }

    [Fact]
    public async Task A_download_is_an_attachment_that_the_browser_will_not_run()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray();
        var id = await BrandKits.UploadAsync(admin.Browser, brandId, svg, "logo.svg", "logo");

        var download = await admin.Browser.GetAsync($"{Files(brandId)}/{id}/download", Token);

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("image/svg+xml", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("logo.svg", download.Content.Headers.ContentDisposition.FileNameStar);
        Assert.Equal("nosniff", Assert.Single(download.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("sandbox", Assert.Single(download.Headers.GetValues("Content-Security-Policy")));
    }

    [Theory]
    [InlineData("brand-guide.pdf")]
    [InlineData("notes.txt")]
    public async Task A_file_whose_bytes_are_not_an_allowed_type_is_refused(string fileName)
    {
        var (admin, brandId) = await WorkspaceAsync();
        var html = Encoding.UTF8.GetBytes("<html><body><script>alert(1)</script></body></html>");

        var response = await admin.Browser.PostAsync(Files(brandId), BrandKits.File(html, fileName), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(new[] { "Upload a PDF, PNG, JPEG, WebP, SVG or Word (.docx) file." }, await BrandKits.ErrorsAsync(response, "file"));
        Assert.Empty((await admin.Browser.GetFromJsonAsync<JsonElement>(Files(brandId), Token)).EnumerateArray());
    }

    [Fact]
    public async Task A_logo_must_be_an_image()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var pdf = await admin.Browser.PostAsync(Files(brandId), BrandKits.File(BrandKits.Pdf, "logo.pdf", "logo"), Token);
        var png = await admin.Browser.PostAsync(Files(brandId), BrandKits.File(BrandKits.Png, "logo.png", "logo"), Token);
        var unknown = await admin.Browser.PostAsync(Files(brandId), BrandKits.File(BrandKits.Png, "logo.png", "banner"), Token);

        Assert.Equal(new[] { "A logo must be a PNG, JPEG, WebP or SVG image." }, await BrandKits.ErrorsAsync(pdf, "file"));
        Assert.Equal(HttpStatusCode.Created, png.StatusCode);
        Assert.Equal("logo", (await png.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("kind").GetString());
        Assert.Equal(new[] { "Use guideline or logo." }, await BrandKits.ErrorsAsync(unknown, "kind"));
    }

    [Fact]
    public async Task A_file_over_20_MB_or_no_file_at_all_is_refused()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var huge = new byte[KitFileTypes.MaxBytes + 1];
        BrandKits.Pdf.CopyTo(huge, 0);

        var tooLarge = await admin.Browser.PostAsync(Files(brandId), BrandKits.File(huge, "huge.pdf"), Token);
        var missing = await admin.Browser.PostAsync(Files(brandId), new MultipartFormDataContent { { new StringContent("x"), "notes" } }, Token);

        Assert.Equal(new[] { "The file is larger than 20 MB." }, await BrandKits.ErrorsAsync(tooLarge, "file"));
        Assert.Equal(new[] { "Choose a file to upload." }, await BrandKits.ErrorsAsync(missing, "file"));
    }

    [Fact]
    public async Task A_draft_can_only_refer_to_this_brands_files_of_the_right_kind()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var guide = await BrandKits.UploadAsync(admin.Browser, brandId, BrandKits.Pdf, "guide.pdf");
        var logo = await BrandKits.UploadAsync(admin.Browser, brandId, BrandKits.Png, "logo.png", "logo");

        var wrongLogo = await BrandKits.PutDraftAsync(admin.Browser, brandId, new { logo = new { fileId = guide } }, 0);
        var unknown = await BrandKits.PutDraftAsync(admin.Browser, brandId, new { guidelineFileIds = new[] { Guid.NewGuid() } }, 0);
        var saved = await BrandKits.SaveAsync(admin.Browser, brandId, new { logo = new { fileId = logo }, guidelineFileIds = new[] { guide } }, 0);

        Assert.Equal(new[] { "Upload the logo to the Brand Kit first." }, await BrandKits.ErrorsAsync(wrongLogo, "content.logo.fileId"));
        Assert.Equal(new[] { "Upload this guideline file to the Brand Kit first." }, await BrandKits.ErrorsAsync(unknown, "content.guidelineFileIds[0]"));
        Assert.Equal(
            new[] { (logo, "logo"), (guide, "guideline") },
            saved.GetProperty("files").EnumerateArray().Select(file => (file.GetProperty("id").GetGuid(), file.GetProperty("kind").GetString()!)).ToArray());
    }

    [Fact]
    public async Task Deleting_a_file_removes_it_from_the_draft_but_not_from_the_approved_version()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var guide = await BrandKits.UploadAsync(admin.Browser, brandId, BrandKits.Pdf, "guide.pdf");
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, new { guidelineFileIds = new[] { guide } });
        var draftRevision = (await BrandKits.SaveAsync(admin.Browser, brandId, new { guidelineFileIds = new[] { guide } }, 0)).GetProperty("revision").GetInt32();

        var deleted = await admin.Browser.DeleteAsync($"{Files(brandId)}/{guide}", Token);
        var again = await admin.Browser.DeleteAsync($"{Files(brandId)}/{guide}", Token);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Empty((await admin.Browser.GetFromJsonAsync<JsonElement>(Files(brandId), Token)).EnumerateArray());
        var kit = await BrandKits.GetAsync(admin.Browser, brandId);
        Assert.Empty(kit.GetProperty("draft").GetProperty("content").GetProperty("guidelineFileIds").EnumerateArray());
        Assert.Equal(draftRevision + 1, kit.GetProperty("draft").GetProperty("revision").GetInt32());
        var approvedFile = Assert.Single(kit.GetProperty("approved").GetProperty("files").EnumerateArray());
        Assert.True(approvedFile.GetProperty("deleted").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await admin.Browser.GetAsync($"{Files(brandId)}/{guide}/download", Token)).StatusCode);
        Assert.Contains("brand_kit.file_deleted", await Workspaces.AuditActionsAsync(admin.Browser, brandId));
    }

    [Fact]
    public async Task Restoring_a_version_keeps_its_deleted_files()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var guide = await BrandKits.UploadAsync(admin.Browser, brandId, BrandKits.Pdf, "guide.pdf");
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, new { guidelineFileIds = new[] { guide } });
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content());
        (await admin.Browser.DeleteAsync($"{Files(brandId)}/{guide}", Token)).EnsureSuccessStatusCode();

        var restored = await admin.Browser.PostAsJsonAsync($"{BrandKits.Kit(brandId)}/versions/1/restore", new { revision = 0 }, Token);
        var draft = await restored.Content.ReadFromJsonAsync<JsonElement>(Token);
        var approved = await BrandKits.ApproveAsync(admin.Browser, brandId, draft.GetProperty("revision").GetInt32());

        Assert.Equal(guide, Assert.Single(approved.GetProperty("files").EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData("marketer")]
    [InlineData("approver")]
    public async Task Only_admins_can_upload_or_delete_but_members_can_read(string role)
    {
        var (admin, brandId) = await WorkspaceAsync();
        var guide = await BrandKits.UploadAsync(admin.Browser, brandId, BrandKits.Pdf, "guide.pdf");
        var member = await Workspaces.JoinAsync(_api, admin.Browser, brandId, role);

        var upload = await member.Browser.PostAsync(Files(brandId), BrandKits.File(BrandKits.Pdf, "guide.pdf"), Token);
        var delete = await member.Browser.DeleteAsync($"{Files(brandId)}/{guide}", Token);
        var list = await member.Browser.GetAsync(Files(brandId), Token);
        var download = await member.Browser.GetAsync($"{Files(brandId)}/{guide}/download", Token);

        Assert.Equal(
            new[] { HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.OK, HttpStatusCode.OK },
            new[] { upload.StatusCode, delete.StatusCode, list.StatusCode, download.StatusCode });
    }

    [Fact]
    public async Task Another_workspace_cannot_download_the_files()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var guide = await BrandKits.UploadAsync(admin.Browser, brandId, BrandKits.Pdf, "guide.pdf");
        var (outsider, outsiderBrandId) = await WorkspaceAsync();

        var direct = await outsider.Browser.GetAsync($"{Files(brandId)}/{guide}/download", Token);
        var throughOwnWorkspace = await outsider.Browser.GetAsync($"{Files(outsiderBrandId)}/{guide}/download", Token);

        Assert.Equal(HttpStatusCode.NotFound, direct.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, throughOwnWorkspace.StatusCode);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static string Files(Guid brandId) => $"{BrandKits.Kit(brandId)}/files";

    private async Task<(Member Admin, Guid BrandId)> WorkspaceAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        return (admin, await Workspaces.CreateAsync(admin.Browser));
    }
}
