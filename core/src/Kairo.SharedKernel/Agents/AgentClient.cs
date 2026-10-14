using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Kairo.SharedKernel.Agents;

public sealed class AgentClient(IHttpClientFactory http, IAgentIdentity identity, IConfiguration configuration)
{
    public const string ClientName = "kairo-agents";
    public const string UrlSetting = "Kairo:Agents:Url";
    public const string AudienceSetting = "Kairo:Agents:Audience";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest body, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (configuration[UrlSetting] is not { Length: > 0 } baseUrl)
        {
            throw new AgentCallException(AgentCallException.NotConfigured, "The agent service's address isn't set.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), path.TrimStart('/')))
        {
            Content = JsonContent.Create(body, options: Json),
        };
        if (configuration[AudienceSetting] is { Length: > 0 } audience)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await identity.GetTokenAsync(audience, cancellationToken));
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            using var response = await http.CreateClient(ClientName).SendAsync(request, deadline.Token);
            var text = await response.Content.ReadAsStringAsync(deadline.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new AgentCallException(AgentCallException.Failed, $"The agent service answered {(int)response.StatusCode}: {Shorten(text)}");
            }

            try
            {
                return JsonSerializer.Deserialize<TResponse>(text, Json)
                    ?? throw new AgentCallException(AgentCallException.BadAnswer, "The agent service sent an empty answer.");
            }
            catch (JsonException exception)
            {
                throw new AgentCallException(AgentCallException.BadAnswer, "The agent service's answer isn't the expected JSON.", exception);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AgentCallException(AgentCallException.Timeout, $"The agent service didn't answer within {timeout.TotalSeconds:0.#} seconds.");
        }
        catch (HttpRequestException exception)
        {
            throw new AgentCallException(AgentCallException.Failed, "Couldn't reach the agent service.", exception);
        }
    }

    private static string Shorten(string text) => text.Length > 300 ? text[..300] : text;
}
