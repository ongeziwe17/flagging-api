using FeatureFlags.Api.Services;

namespace FeatureFlags.Api.Auth;

public class FeatureGateFilter : IEndpointFilter
{
    private readonly string _flagKey;
    private readonly string _envHeader;
    private readonly string _userHeader;

    public FeatureGateFilter(string flagKey, string envHeader = "X-Env", string userHeader = "X-User-Id")
        => (_flagKey, _envHeader, _userHeader) = (flagKey, envHeader, userHeader);

    // NOTE: object? to match IEndpointFilter in .NET 7/8
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var req = ctx.HttpContext.Request;
        string env = req.Headers[_envHeader].FirstOrDefault() ?? "prod";
        string user = req.Headers[_userHeader].FirstOrDefault() ?? "anonymous";

        var eval = ctx.HttpContext.RequestServices.GetRequiredService<IFlagEvaluationService>();
        var res = await eval.EvaluateAsync(_flagKey, env, user, null);
        if (!res.Enabled) return Results.StatusCode(StatusCodes.Status404NotFound); // or Results.Forbid()

        return await next(ctx);
    }
}

public static class FeatureGateExtensions
{
    public static RouteHandlerBuilder RequireFlag(this RouteHandlerBuilder b, string flagKey,
        string envHeader = "X-Env", string userHeader = "X-User-Id")
        => b.AddEndpointFilter(new FeatureGateFilter(flagKey, envHeader, userHeader));
}
