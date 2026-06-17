using DNA.Email.Core.Options;
using Microsoft.Extensions.Options;

namespace DNA.Email.API.Middleware;

public sealed class ApiKeyMiddleware(RequestDelegate next, IOptions<ApiKeyOptions> options)
{
    private readonly ApiKeyOptions _opts = options.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsPublicPath(context.Request.Path))
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(_opts.HeaderName, out var keyValue) ||
            string.IsNullOrWhiteSpace(keyValue))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync($$"""
                {"status":401,"error":"Unauthorized","message":"The '{{_opts.HeaderName}}' header is missing.","timestamp":"{{DateTime.UtcNow:O}}"}
                """);
            return;
        }

        var entry = _opts.Keys.FirstOrDefault(k =>
            string.Equals(k.Key, keyValue, StringComparison.Ordinal));

        if (entry is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync($$"""
                {"status":401,"error":"Unauthorized","message":"Invalid API key.","timestamp":"{{DateTime.UtcNow:O}}"}
                """);
            return;
        }

        context.Items["ApiKeyEntry"] = entry;
        await next(context);
    }

    private static bool IsPublicPath(PathString path) =>
        path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/scalar", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/openapi", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/", StringComparison.OrdinalIgnoreCase);
}
