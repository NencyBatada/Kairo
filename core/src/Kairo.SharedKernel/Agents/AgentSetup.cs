using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kairo.SharedKernel.Agents;

public static class AgentSetup
{
    public static readonly TimeSpan MaxTimeout = TimeSpan.FromMinutes(9);

    public static IServiceCollection AddKairoAgents(this IServiceCollection services)
    {
        services.AddHttpClient(AgentClient.ClientName, client => client.Timeout = MaxTimeout).RemoveAllResilienceHandlers();
        services.TryAddSingleton<IAgentIdentity, GoogleAgentIdentity>();
        services.TryAddSingleton<AgentClient>();
        services.TryAddSingleton<PlanAgent>();
        services.TryAddSingleton<ProofPlanAgent>();
        return services;
    }
}
