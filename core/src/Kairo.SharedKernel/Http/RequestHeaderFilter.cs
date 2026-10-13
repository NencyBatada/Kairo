using Microsoft.AspNetCore.Http;

namespace Kairo.SharedKernel.Http;

internal sealed class RequestHeaderFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method)
            || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method)
            || request.Headers.ContainsKey(KairoApi.RequestHeader))
        {
            return next(context);
        }

        return ValueTask.FromResult<object?>(TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: $"The {KairoApi.RequestHeader} header is required."));
    }
}
