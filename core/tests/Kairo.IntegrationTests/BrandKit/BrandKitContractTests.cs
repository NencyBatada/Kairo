using Kairo.Contracts.BrandKit;
using Kairo.IntegrationTests.Api;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.BrandKit;

public sealed class BrandKitContractTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task There_is_no_approved_kit_until_one_is_approved()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var before = await ApprovedAsync(brandId);
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content(), 0);

        var withDraftOnly = await ApprovedAsync(brandId);

        Assert.Null(before);
        Assert.Null(withDraftOnly);
    }

    [Fact]
    public async Task Agents_get_the_approved_version_and_never_the_draft()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("approved words"));
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content("draft words"), 0);

        var kit = await ApprovedAsync(brandId);

        Assert.NotNull(kit);
        Assert.Equal(1, kit.Version);
        Assert.Equal("approved words", kit.Content.GetProperty("voice").GetProperty("summary").GetString());
        Assert.Equal("primary", kit.Content.GetProperty("colours")[0].GetProperty("role").GetString());
        Assert.Equal("cheap", kit.Content.GetProperty("rules").GetProperty("bannedWords")[0].GetString());
    }

    [Fact]
    public async Task The_kits_files_come_with_it_logo_first()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var guide = await BrandKits.UploadAsync(admin.Browser, brandId, BrandKits.Pdf, "guide.pdf");
        var logo = await BrandKits.UploadAsync(admin.Browser, brandId, BrandKits.Png, "logo.png", "logo");
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, new { logo = new { fileId = logo }, guidelineFileIds = new[] { guide } });

        var kit = await ApprovedAsync(brandId);

        Assert.Equal(
            new[] { new BrandKitFile(logo, "logo", "logo.png", "image/png"), new BrandKitFile(guide, "guideline", "guide.pdf", "application/pdf") },
            kit!.Files);
    }

    [Fact]
    public async Task A_deleted_file_still_reaches_agents_through_the_approved_version()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var guide = await BrandKits.UploadAsync(admin.Browser, brandId, BrandKits.Pdf, "guide.pdf");
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, new { guidelineFileIds = new[] { guide } });

        (await admin.Browser.DeleteAsync($"{BrandKits.Kit(brandId)}/files/{guide}", Token)).EnsureSuccessStatusCode();

        Assert.Equal(guide, Assert.Single((await ApprovedAsync(brandId))!.Files).Id);
    }

    [Fact]
    public async Task Each_workspace_gets_only_its_own_kit()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content());
        var (_, otherBrandId) = await WorkspaceAsync();

        Assert.Null(await ApprovedAsync(otherBrandId));
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private async Task<ApprovedBrandKit?> ApprovedAsync(Guid brandId)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandId, Token);
        return await scope.ServiceProvider.GetRequiredService<IBrandKit>().GetApprovedAsync(Token);
    }

    private async Task<(Member Admin, Guid BrandId)> WorkspaceAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        return (admin, await Workspaces.CreateAsync(admin.Browser));
    }
}
