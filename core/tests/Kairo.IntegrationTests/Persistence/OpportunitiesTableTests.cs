using Microsoft.Data.SqlClient;

namespace Kairo.IntegrationTests.Persistence;

public sealed class OpportunitiesTableTests(SqlServer sqlServer)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("opportunities.Opportunities")]
    [InlineData("opportunities.WatchTopics")]
    [InlineData("opportunities.RefreshRuns")]
    [InlineData("campaign_rooms.Rooms")]
    [InlineData("campaign_rooms.StageRuns")]
    [InlineData("campaign_rooms.Briefs")]
    [InlineData("triggers.TriggerRules")]
    [InlineData("triggers.TriggerFirings")]
    [InlineData("triggers.FestivalEvents")]
    [InlineData("triggers.BrandStates")]
    public async Task The_table_is_covered_by_its_schemas_security_policy(string table)
    {
        var predicates = await ScalarAsync(
            $"SELECT COUNT(*) FROM sys.security_predicates WHERE target_object_id = OBJECT_ID(N'{table}')",
            Guid.Empty,
            Guid.Empty);

        Assert.Equal(3, predicates);
    }

    [Fact]
    public async Task Weather_forecasts_are_shared_by_every_workspace()
    {
        var predicates = await ScalarAsync(
            "SELECT COUNT(*) FROM sys.security_predicates WHERE target_object_id = OBJECT_ID(N'triggers.WeatherForecasts')",
            Guid.Empty,
            Guid.Empty);

        Assert.Equal(0, predicates);
    }

    [Fact]
    public async Task Another_workspace_cannot_see_a_watched_topic()
    {
        var brandId = Guid.NewGuid();
        await InsertTopicAsync(brandId, "rain");

        var seen = await ScalarAsync(
            """
            EXEC sys.sp_set_session_context @key = N'kairo.brand_id', @value = @other;
            SELECT COUNT(*) FROM opportunities.WatchTopics WHERE BrandId = @brand;
            """,
            brandId,
            Guid.NewGuid());

        Assert.Equal(0, seen);
    }

    [Fact]
    public async Task A_workspace_lists_a_topic_once_whatever_its_case()
    {
        var brandId = Guid.NewGuid();
        await InsertTopicAsync(brandId, "Rain boots");

        var duplicate = await Assert.ThrowsAsync<SqlException>(() => InsertTopicAsync(brandId, "rain BOOTS"));

        Assert.Equal(2601, duplicate.Number);
    }

    [Fact]
    public async Task An_opportunity_opens_at_most_one_campaign_room()
    {
        var brandId = Guid.NewGuid();
        var opportunityId = Guid.NewGuid();
        await InsertRoomAsync(brandId, opportunityId);

        var duplicate = await Assert.ThrowsAsync<SqlException>(() => InsertRoomAsync(brandId, opportunityId));

        Assert.Equal(2601, duplicate.Number);
    }

    private Task InsertTopicAsync(Guid brandId, string phrase) => ScalarAsync(
        $"""
        EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
        INSERT INTO opportunities.WatchTopics (Id, BrandId, Phrase, Kind, CreatedBy, CreatedAt)
        VALUES (NEWID(), @brand, N'{phrase}', N'watch', NEWID(), SYSDATETIMEOFFSET());
        SELECT 0;
        """,
        brandId,
        Guid.Empty);

    private Task InsertRoomAsync(Guid brandId, Guid opportunityId) => ScalarAsync(
        """
        EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
        INSERT INTO campaign_rooms.Rooms (Id, BrandId, Title, State, OpportunityId, Evidence, CreatedBy, CreatedAt)
        VALUES (NEWID(), @brand, N'Monsoon footwear', N'brief_draft', @other, N'{}', NEWID(), SYSDATETIMEOFFSET());
        SELECT 0;
        """,
        brandId,
        opportunityId);

    private async Task<int> ScalarAsync(string sql, Guid brandId, Guid other)
    {
        await using var connection = new SqlConnection(sqlServer.KairoConnectionString);
        await connection.OpenAsync(Token);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@brand", brandId);
        command.Parameters.AddWithValue("@other", other);
        return (int)(await command.ExecuteScalarAsync(Token))!;
    }
}
