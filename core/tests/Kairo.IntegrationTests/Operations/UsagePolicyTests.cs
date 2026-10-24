using Kairo.Contracts.Operations;
using Kairo.Modules.Operations;
using Kairo.SharedKernel.Agents;
using Microsoft.Extensions.Configuration;

namespace Kairo.IntegrationTests.Operations;

public sealed class UsagePolicyTests
{
    [Fact]
    public void Limits_default_and_invalid_configuration_fails_closed()
    {
        Assert.Equal(new GenerationCapacity(100, 50, 10), UsagePolicy.Limits(new ConfigurationBuilder().Build()));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Kairo:Operations:Limits:Images"] = "-1" }).Build();
        Assert.Throws<InvalidOperationException>(() => UsagePolicy.Limits(configuration));
    }

    [Theory]
    [InlineData("text", 1, 0, 0)]
    [InlineData("image", 0, 1, 0)]
    [InlineData("video", 0, 0, 1)]
    public void Every_generation_dimension_enforces_its_own_cap(string operation, int text, int images, int videos)
    {
        var request = new ReserveCall(Guid.NewGuid(), "model", operation, "global", text, images, videos);
        var failure = Assert.Throws<UsageFailure>(() => UsagePolicy.Enforce(new(100, 50, 10), request, new(100, 50, 10)));
        Assert.Equal("generation_limit_exceeded", failure.Code);
    }

    [Fact]
    public void Reservation_dimensions_cannot_be_omitted_or_mislabeled()
    {
        Assert.Throws<UsageFailure>(() => UsagePolicy.Validate(new ReserveCall(Guid.NewGuid(), "model", "text", "global")));
        Assert.Throws<UsageFailure>(() => UsagePolicy.Validate(new ReserveCall(Guid.NewGuid(), "model", "image", "global", TextCalls: 1, Images: 1)));
        Assert.Throws<UsageFailure>(() => UsagePolicy.Validate(new ReserveCall(Guid.NewGuid(), "model", "video", "global", VideoClips: 1)));
    }

    [Fact]
    public void Unknown_usage_and_prices_are_distinct_from_known_zero()
    {
        var at = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        var request = new ReserveCall(Guid.NewGuid(), "gemini-3.8-flash", "text", "global", TextCalls: 1);
        Assert.Null(UsageRates.Estimate(request, new("succeeded"), at).Cost);
        Assert.Equal(0m, UsageRates.Estimate(request, new("succeeded", InputTokens: 0, OutputTokens: 0, ThoughtTokens: 0, CachedTokens: 0), at).Cost);
        Assert.Null(UsageRates.Estimate(request with { Model = "unknown-model" }, new("succeeded", InputTokens: 0, OutputTokens: 0, ThoughtTokens: 0, CachedTokens: 0), at).Cost);
        Assert.Null(UsageRates.Estimate(request, new("uncertain"), at).Cost);
    }

    [Fact]
    public void Rates_include_reasoning_cached_tokens_region_and_scheduled_price_change()
    {
        var request = new ReserveCall(Guid.NewGuid(), "gemini-3.8-flash", "text", "global", TextCalls: 1);
        var report = new ReportCall("succeeded", InputTokens: 1_000_000, OutputTokens: 1_000_000, ThoughtTokens: 1_000_000, CachedTokens: 100_000);
        var before = new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero);
        var estimated = UsageRates.Estimate(request, report, before);
        Assert.Equal(8.1825m, estimated.Cost);
        Assert.Equal(UsageRates.Version, estimated.Version);
        Assert.Equal(estimated.Cost * 1.1m, UsageRates.Estimate(request with { Region = "asia-south1" }, report, before).Cost);
        Assert.Equal(estimated.Cost * 2m, UsageRates.Estimate(request, report, before.AddSeconds(1)).Cost);
    }

    [Fact]
    public void Invocation_scope_restores_nested_campaign_context()
    {
        var context = new AgentUsageContext();
        var first = new AgentUsageScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        using (context.Enter(first))
        {
            using (context.Enter(new(Guid.NewGuid()))) Assert.NotEqual(first, context.Current);
            Assert.Equal(first, context.Current);
        }
        Assert.Null(context.Current);
    }
}
