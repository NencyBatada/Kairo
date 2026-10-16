using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Kairo.SharedKernel.Files;

internal sealed class GoogleCloudFileStore(IHttpClientFactory http, IStorageAccessToken token, FileStoreOptions options) : IFileStore
{
    private const string Api = "https://storage.googleapis.com";
    private const int MaxRewriteCalls = 20;

    public async Task PutAsync(string bucket, string key, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        FileKeys.EnsureValid(bucket, key);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{Api}/upload/storage/v1/b/{options.BucketName(bucket)}/o?uploadType=media&name={Uri.EscapeDataString(key)}")
        {
            Content = new ByteArrayContent(content),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var response = await SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        await EnsureSuccessAsync(response, bucket, key, cancellationToken);
    }

    public async Task<Stream> OpenAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        FileKeys.EnsureValid(bucket, key);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{Api}/storage/v1/b/{options.BucketName(bucket)}/o/{Uri.EscapeDataString(key)}?alt=media");
        var response = await SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            throw new StoredFileMissingException(bucket, key);
        }

        if (!response.IsSuccessStatusCode)
        {
            using (response)
            {
                await EnsureSuccessAsync(response, bucket, key, cancellationToken);
            }
        }

        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }

    public async Task CopyAsync(string bucket, string fromKey, string toKey, CancellationToken cancellationToken)
    {
        FileKeys.EnsureValid(bucket, fromKey);
        FileKeys.EnsureValid(bucket, toKey);
        var name = options.BucketName(bucket);
        string? rewriteToken = null;
        for (var call = 0; call < MaxRewriteCalls; call++)
        {
            var url = $"{Api}/storage/v1/b/{name}/o/{Uri.EscapeDataString(fromKey)}/rewriteTo/b/{name}/o/{Uri.EscapeDataString(toKey)}"
                + (rewriteToken is null ? "" : $"?rewriteToken={Uri.EscapeDataString(rewriteToken)}");
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            using var response = await SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new StoredFileMissingException(bucket, fromKey);
            }

            await EnsureSuccessAsync(response, bucket, fromKey, cancellationToken);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (body.RootElement.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True)
            {
                return;
            }

            rewriteToken = body.RootElement.TryGetProperty("rewriteToken", out var token) && token.ValueKind == JsonValueKind.String
                ? token.GetString()
                : throw new FileStoreException($"Cloud Storage didn't finish copying {bucket}/{fromKey} and gave no token to continue.");
        }

        throw new FileStoreException($"Cloud Storage didn't finish copying {bucket}/{fromKey}.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completion, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await token.GetAsync(cancellationToken));
        return await http.CreateClient(FileStoreSetup.HttpClientName).SendAsync(request, completion, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string bucket, string key, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new FileStoreException(
            $"Cloud Storage answered {(int)response.StatusCode} for {bucket}/{key}: {(body.Length > 300 ? body[..300] : body)}");
    }
}
