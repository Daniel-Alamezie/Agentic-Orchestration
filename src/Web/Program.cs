using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Extensions;
using Core.Interfaces;
using Core.Models;
using Patterns;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddOllamaKernel(builder.Configuration);
builder.Services.AddPatternRegistry();
builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

// ── API endpoints ─────────────────────────────────────────────────────────────

/// GET /api/patterns — returns metadata for all patterns (for the sidebar)
app.MapGet("/api/patterns", (PatternRegistry registry) =>
    Results.Ok(registry.GetAllInfo()));

/// GET /api/run?pattern={id}&prompt={text}
/// Streams AgentEvents as Server-Sent Events (SSE) so the UI can show
/// each agent's messages arriving in real time.
app.MapGet("/api/run", async (
    HttpContext ctx,
    PatternRegistry registry,
    string pattern,
    string prompt,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(prompt))
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsync("pattern and prompt query params are required.");
        return;
    }

    IPatternRunner runner;
    try { runner = registry.Get(pattern); }
    catch (KeyNotFoundException)
    {
        ctx.Response.StatusCode = 404;
        await ctx.Response.WriteAsync($"Unknown pattern: {pattern}");
        return;
    }

    // SSE headers — keep the connection alive for streaming
    ctx.Response.Headers.ContentType  = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection   = "keep-alive";
    ctx.Response.Headers["X-Accel-Buffering"] = "no"; // disable nginx buffering

    var jsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }   // sends "AgentThinking" not 0
    };

    try
    {
        await foreach (var evt in runner.RunAsync(prompt, ct))
        {
            var data = JsonSerializer.Serialize(evt, jsonOptions);
            await ctx.Response.WriteAsync($"data: {data}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        }
    }
    catch (OperationCanceledException)
    {
        // Client disconnected — normal
    }
    catch (Exception ex)
    {
        var errEvt = AgentEvent.Error($"Server error: {ex.Message}");
        var data   = JsonSerializer.Serialize(errEvt, jsonOptions);
        await ctx.Response.WriteAsync($"data: {data}\n\n", CancellationToken.None);
        await ctx.Response.Body.FlushAsync(CancellationToken.None);
    }
    finally
    {
        // Signal the stream is done
        await ctx.Response.WriteAsync("data: [DONE]\n\n", CancellationToken.None);
        await ctx.Response.Body.FlushAsync(CancellationToken.None);
    }
});

app.Run();
