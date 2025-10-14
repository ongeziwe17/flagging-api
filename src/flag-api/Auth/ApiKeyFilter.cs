using FeatureFlags.Api.Data;
using FeatureFlags.Api.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace FeatureFlags.Api.Auth;

public class ApiKeyFilter : IEndpointFilter
{
    private readonly string _role; // "Reader" or "Admin"
    public ApiKeyFilter(string role) => _role = role;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var req = context.HttpContext.Request;
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();

        var key = req.Headers["X-API-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key)) return TypedResults.Unauthorized();

        var hash = HashUtil.Sha256Hex(key);
        var apiKey = await db.ApiKeys.FirstOrDefaultAsync(x => x.KeyHash == hash && !x.IsRevoked);
        if (apiKey is null) return TypedResults.Unauthorized();

        if (_role.Equals("Admin", StringComparison.OrdinalIgnoreCase) &&
            !apiKey.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            return TypedResults.Forbid();

        // Attach for audit
        context.HttpContext.Items["ApiKeyName"] = apiKey.Name;
        return await next(context);
    }
}

public static class ApiKeyFilterExtensions
{
    public static RouteHandlerBuilder RequireApiKey(this RouteHandlerBuilder builder, string role = "Reader")
        => builder.AddEndpointFilter(new ApiKeyFilter(role));
}
