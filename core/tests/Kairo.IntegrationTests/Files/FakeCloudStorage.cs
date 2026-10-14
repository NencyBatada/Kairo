using System.Net;
using System.Net.Http.Headers;
using Kairo.SharedKernel.Files;
using Microsoft.AspNetCore.WebUtilities;

namespace Kairo.IntegrationTests.Files;

internal sealed class FakeStorageToken : IStorageAccessToken
{
    public const string Value = "storage-token";

    public Task<string> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Value);
}

internal sealed class FakeCloudStorage : HttpMessageHandler
{
    public Dictionary<string, (byte[] Content, string ContentType)> Objects { get; } = [];

    public List<string> Calls { get; } = [];

    public HttpStatusCode? FailWith { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var raw = request.RequestUri!.OriginalString;
        Calls.Add($"{request.Method} {raw} {request.Headers.Authorization}");
        if (FailWith is { } status)
        {
            return new HttpResponseMessage(status) { Content = new StringContent("{\"error\":{\"message\":\"unavailable\"}}") };
        }

        var path = request.RequestUri.AbsolutePath.Split('/');
        if (request.Method == HttpMethod.Post && path is ["", "upload", "storage", "v1", "b", var bucket, "o"])
        {
            var name = QueryHelpers.ParseQuery(request.RequestUri.Query)["name"].ToString();
            Objects[$"{bucket}/{name}"] = (await request.Content!.ReadAsByteArrayAsync(cancellationToken), request.Content.Headers.ContentType!.MediaType!);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }

        if (request.Method == HttpMethod.Get && path is ["", "storage", "v1", "b", var readBucket, "o", ..])
        {
            var start = raw.IndexOf("/o/", StringComparison.Ordinal) + 3;
            var end = raw.IndexOf('?', start);
            var key = Uri.UnescapeDataString(raw[start..(end < 0 ? raw.Length : end)]);
            if (!Objects.TryGetValue($"{readBucket}/{key}", out var stored))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            }

            var content = new ByteArrayContent(stored.Content);
            content.Headers.ContentType = new MediaTypeHeaderValue(stored.ContentType);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }

        return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent($"Unexpected call {request.Method} {raw}") };
    }
}
