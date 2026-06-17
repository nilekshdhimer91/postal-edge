using DNA.Email.API.Middleware;
using DNA.Email.Core.Extensions;
using DNA.Email.Core.Options;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using DnaApiKeyOptions = DNA.Email.Core.Options.ApiKeyOptions;

var builder = WebApplication.CreateBuilder(args);

// ── Controllers ───────────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(o =>
{
    o.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                e => e.Key,
                e => e.Value!.Errors.Select(x => x.ErrorMessage).ToArray());

        var result = DNA.Email.Core.Models.Responses.EmailSendResult.Fail(
            new DNA.Email.Core.Models.Responses.EmailSendError
            {
                Code = "VALIDATION_ERROR",
                Message = "One or more validation errors occurred.",
                ValidationErrors = errors
            });

        return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(result);
    };
});

// ── Core email services ───────────────────────────────────────────────────────
builder.Services.AddDnaEmailCore(builder.Configuration);

// ── OpenAPI / Scalar ──────────────────────────────────────────────────────────
builder.Services.AddOpenApi();

// ── Rate limiting ─────────────────────────────────────────────────────────────
builder.Services.AddRateLimiter(o =>
{
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var apiKeyOpts = context.RequestServices
            .GetRequiredService<IOptions<DnaApiKeyOptions>>().Value;

        var keyHeader = context.Request.Headers[apiKeyOpts.HeaderName].FirstOrDefault();
        var entry = keyHeader is not null
            ? apiKeyOpts.Keys.FirstOrDefault(k => string.Equals(k.Key, keyHeader, StringComparison.Ordinal))
            : null;

        if (entry?.RateLimit is { } rl)
        {
            return RateLimitPartition.GetFixedWindowLimiter(entry.Key, _ =>
                new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rl.PermitLimit,
                    Window = TimeSpan.FromSeconds(rl.WindowSeconds),
                    QueueLimit = rl.QueueLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                });
        }

        // IP-based fallback for unauthenticated / unknown keys
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });

    o.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        ctx.HttpContext.Response.ContentType = "application/json";
        await ctx.HttpContext.Response.WriteAsync(
            """{"status":429,"error":"Too Many Requests","message":"Rate limit exceeded. Please slow down."}""", ct);
    };
});

// ── CORS ──────────────────────────────────────────────────────────────────────
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["*"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (allowedOrigins.Contains("*"))
        p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    else
        p.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader();
}));

// ── Request size limit ────────────────────────────────────────────────────────
var maxBytes = builder.Configuration.GetValue<long>("Limits:MaxRequestBodyBytes", 52_428_800);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = maxBytes);

var app = builder.Build();

// ── Pipeline ──────────────────────────────────────────────────────────────────
app.UseRateLimiter();
app.UseMiddleware<ApiKeyMiddleware>();
app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(o => o
        .WithTitle("postal-edge — DNA Email API")
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
        .AddPreferredSecuritySchemes("ApiKey"));

    app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();

    var urls = app.Urls.FirstOrDefault() ?? "http://localhost:5000";
    app.Logger.LogInformation("Scalar docs: {Url}/scalar/v1", urls);
}

app.MapControllers();

app.Run();
