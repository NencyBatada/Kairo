using System.Net;
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.IntegrationTests.Feeds;

public sealed class GoogleAccessErrorsTests
{
    [Theory]
    [InlineData("USER_PERMISSION_DENIED")]
    [InlineData("CUSTOMER_NOT_ENABLED")]
    public void A_Google_Ads_authorization_error_about_the_account_means_access_was_lost(string code)
    {
        Assert.True(GoogleAccessErrors.MeansAccessLost(HttpStatusCode.Forbidden, AdsFailure(code)));
    }

    [Theory]
    [InlineData("DEVELOPER_TOKEN_NOT_APPROVED")]
    [InlineData("ACTION_NOT_PERMITTED")]
    public void Other_Google_Ads_authorization_errors_are_not_access_loss(string code)
    {
        Assert.False(GoogleAccessErrors.MeansAccessLost(HttpStatusCode.Forbidden, AdsFailure(code)));
    }

    [Fact]
    public void A_permission_denied_answer_means_access_was_lost()
    {
        const string body = """{"error":{"code":403,"message":"User does not have sufficient permissions for this property.","status":"PERMISSION_DENIED"}}""";

        Assert.True(GoogleAccessErrors.MeansAccessLost(HttpStatusCode.Forbidden, body));
    }

    [Fact]
    public void A_BigQuery_access_denied_answer_means_access_was_lost()
    {
        const string body = """{"error":{"code":403,"message":"Access Denied: Table acme-data:sales.orders","errors":[{"message":"Access Denied","domain":"global","reason":"accessDenied"}]}}""";

        Assert.True(GoogleAccessErrors.MeansAccessLost(HttpStatusCode.Forbidden, body));
    }

    [Theory]
    [InlineData("""{"error":{"code":403,"message":"API disabled","status":"PERMISSION_DENIED","details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"SERVICE_DISABLED"}]}}""")]
    [InlineData("""{"error":{"code":403,"message":"Quota exceeded","errors":[{"reason":"quotaExceeded"}],"status":"PERMISSION_DENIED"}}""")]
    [InlineData("""{"error":{"code":403,"message":"Rate limit","errors":[{"reason":"rateLimitExceeded"}]}}""")]
    [InlineData("")]
    [InlineData("not json")]
    public void Forbidden_answers_about_quota_setup_or_with_no_detail_are_not_access_loss(string body)
    {
        Assert.False(GoogleAccessErrors.MeansAccessLost(HttpStatusCode.Forbidden, body));
    }

    [Fact]
    public void Server_errors_are_not_access_loss()
    {
        const string body = """[{"error":{"code":500,"message":"Internal error encountered.","status":"INTERNAL"}}]""";

        Assert.False(GoogleAccessErrors.MeansAccessLost(HttpStatusCode.InternalServerError, body));
    }

    [Fact]
    public void The_exception_carries_the_classification()
    {
        Assert.True(new GoogleApiException(HttpStatusCode.Forbidden, AdsFailure("USER_PERMISSION_DENIED")).AccessLost);
        Assert.False(new GoogleApiException(HttpStatusCode.BadGateway, "").AccessLost);
    }

    private static string AdsFailure(string code) =>
        $$$"""[{"error":{"code":403,"message":"The caller does not have permission","status":"PERMISSION_DENIED","details":[{"@type":"type.googleapis.com/google.ads.googleads.v25.errors.GoogleAdsFailure","errors":[{"errorCode":{"authorizationError":"{{{code}}}"},"message":"denied"}]}]}}]""";
}
