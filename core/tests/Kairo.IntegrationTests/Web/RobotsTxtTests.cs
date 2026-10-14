using Kairo.SharedKernel.Http;

namespace Kairo.IntegrationTests.Web;

public sealed class RobotsTxtTests
{
    [Fact]
    public void An_empty_file_allows_everything()
    {
        Assert.True(RobotsTxt.Parse("").Allows("/anything"));
        Assert.True(RobotsTxt.AllowAll.Allows("/anything"));
    }

    [Fact]
    public void Kairo_follows_the_group_for_every_robot()
    {
        var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /account\n");

        Assert.False(robots.Allows("/account/orders"));
        Assert.True(robots.Allows("/about"));
    }

    [Fact]
    public void Kairo_prefers_a_group_that_names_it()
    {
        var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /\n\nUser-agent: KairoBot\nDisallow: /private\n");

        Assert.True(robots.Allows("/about"));
        Assert.False(robots.Allows("/private/notes"));
    }

    [Fact]
    public void The_longest_rule_wins_and_allow_wins_a_tie()
    {
        var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /shop\nAllow: /shop/shoes\nDisallow: /a\nAllow: /a\n");

        Assert.False(robots.Allows("/shop/bags"));
        Assert.True(robots.Allows("/shop/shoes/running"));
        Assert.True(robots.Allows("/a"));
    }

    [Fact]
    public void Wildcards_and_end_anchors_work()
    {
        var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /*.pdf$\nDisallow: /search*q=\n");

        Assert.False(robots.Allows("/files/guide.pdf"));
        Assert.True(robots.Allows("/files/guide.pdf?download=1"));
        Assert.False(robots.Allows("/search?q=shoes"));
    }

    [Fact]
    public void A_group_can_name_several_robots_and_comments_are_ignored()
    {
        var robots = RobotsTxt.Parse("User-agent: Googlebot\nUser-agent: KairoBot # that's us\nDisallow: /team # private\n");

        Assert.False(robots.Allows("/team"));
    }

    [Fact]
    public void An_empty_disallow_allows_everything()
    {
        Assert.True(RobotsTxt.Parse("User-agent: *\nDisallow:\n").Allows("/x"));
    }
}
