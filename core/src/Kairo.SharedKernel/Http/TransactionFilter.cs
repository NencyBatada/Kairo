using Kairo.SharedKernel.Persistence;
using Microsoft.AspNetCore.Http;

namespace Kairo.SharedKernel.Http;

internal sealed class TransactionFilter(KairoDatabase database) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method)
            || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method)
            || context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<NoRequestTransaction>() is not null)
        {
            return await next(context);
        }

        var cancellationToken = context.HttpContext.RequestAborted;
        await database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await next(context);
            if (IsSuccess(result))
            {
                await database.CommitAsync(cancellationToken);
            }
            else
            {
                await database.RollbackAsync(cancellationToken);
            }

            return result;
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static bool IsSuccess(object? result) => result switch
    {
        INestedHttpResult nested => IsSuccess(nested.Result),
        IStatusCodeHttpResult { StatusCode: >= 400 } => false,
        _ => true,
    };
}
