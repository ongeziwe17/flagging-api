using System.Text.Json;
using FeatureFlags.Api.Auth;
using FeatureFlags.Api.Data;
using FeatureFlags.Api.Models;
using FeatureFlags.Api.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Serilog;

// Use fully-qualified Serilog.Log to avoid any name collisions
global::Serilog.Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("error.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Warning)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddMemoryCache();
builder.Services.AddStackExchangeRedisCache(o =>
    o.Configuration = builder.Configuration.GetConnectionString("Redis"));

builder.Services.AddScoped<IFlagEvaluationService, FlagEvaluationService>();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.WriteIndented = true;
});

builder.Services.AddRouting(o => o.LowercaseUrls = true);
builder.Services.AddEndpointsApiExplorer();

builder.WebHost.UseUrls("http://0.0.0.0:8080");

var app = builder.Build();

app.UseSerilogRequestLogging();

app.UseDefaultFiles();
app.UseStaticFiles();

// Ensure DB + seed
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    const int maxDbRetries = 30;
    var retryDelay = TimeSpan.FromSeconds(5);
    var dbReady = false;
    Exception? lastDbException = null;

    for (var attempt = 1; attempt <= maxDbRetries && !dbReady; attempt++)
    {
        try
        {
            await db.Database.MigrateAsync();
            startupLogger.LogInformation("Database migrations completed on attempt {Attempt}.", attempt);
            dbReady = true;
        }
        catch (Exception ex) when (IsConnectivityError(ex) && attempt < maxDbRetries)
        {
            lastDbException = ex;
            startupLogger.LogWarning(ex,
                "Database migration failed (attempt {Attempt}/{Max}). Waiting {DelaySeconds}s before retrying.",
                attempt, maxDbRetries, retryDelay.TotalSeconds);
            await Task.Delay(retryDelay);
        }
        catch (Exception ex) when (IsConnectivityError(ex))
        {
            lastDbException = ex;
            break;
        }
    }

    if (!dbReady)
    {
        startupLogger.LogCritical(lastDbException,
            "Unable to run database migrations after {Max} attempts. The application cannot start.",
            maxDbRetries);
        throw lastDbException ?? new InvalidOperationException("Database migrations failed before startup.");
    }

    async Task EnsureEnv(string key, string name)
    {
        if (!await db.Environments.AnyAsync(x => x.Key == key))
            db.Environments.Add(new EnvironmentEntity { Key = key, Name = name });
    }
    await EnsureEnv("dev", "Development");
    await EnsureEnv("staging", "Staging");
    await EnsureEnv("prod", "Production");

    if (!await db.ApiKeys.AnyAsync())
    {
        var bootstrap = app.Configuration["Admin:BootstrapAdminKey"];
        if (!string.IsNullOrWhiteSpace(bootstrap) && bootstrap != "replace-me-on-first-run")
        {
            db.ApiKeys.Add(new ApiKey { Name = "Bootstrap Admin", Role = "Admin", KeyHash = HashUtil.Sha256Hex(bootstrap) });
            Console.WriteLine("[FeatureFlags] Bootstrapped Admin API key.");
        }
        else
        {
            Console.WriteLine("[FeatureFlags] Set Admin:BootstrapAdminKey before first run.");
        }
    }

    await db.SaveChangesAsync();
}

// Public evaluation
var eval = app.MapGroup("/api/evaluate");
eval.MapPost("/{flagKey}", async (string flagKey, EvalRequest? req, IFlagEvaluationService evaluator, ILogger<Program> logger) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(flagKey) || req is null)
            return Results.BadRequest(new { message = "flagKey and request body are required." });

        if (string.IsNullOrWhiteSpace(req.EnvKey))
            return Results.BadRequest(new { message = "EnvKey is required." });

        if (string.IsNullOrWhiteSpace(req.UserId))
            return Results.BadRequest(new { message = "UserId is required." });

        var res = await evaluator.EvaluateAsync(flagKey, req.EnvKey, req.UserId, req.Attributes);
        return Results.Ok(res);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "evaluate_failed flag={Flag} env={Env} user={User}", flagKey, req?.EnvKey, req?.UserId);
        return Results.Problem(title: "Failed to evaluate flag", statusCode: StatusCodes.Status500InternalServerError);
    }
});


// Flags (admin/reader)
var flags = app.MapGroup("/api/flags");

flags.MapGet("", async (string? envKey, AppDbContext db) =>
{
    var q = db.FeatureFlags.AsQueryable().Where(f => !f.IsArchived);
    var list = await q.Select(f => new
    {
        f.Key,
        f.Name,
        f.Description,
        environments = f.Assignments.Select(a => new { envKey = a.Environment!.Key, a.IsEnabled, a.PercentageRollout })
    }).ToListAsync();

    if (!string.IsNullOrWhiteSpace(envKey))
        list = list.Select(f => new { f.Key, f.Name, f.Description, environments = f.environments.Where(e => e.envKey == envKey) }).ToList();

    return Results.Ok(list);
}).RequireApiKey("Reader");

flags.MapGet("/{key}", async (string key, AppDbContext db) =>
{
    var flag = await db.FeatureFlags.Include(f => f.Assignments).ThenInclude(a => a.Environment)
        .SingleOrDefaultAsync(f => f.Key == key);
    return flag is null ? Results.NotFound() : Results.Ok(new
    {
        flag.Key,
        flag.Name,
        flag.Description,
        flag.IsArchived,
        assignments = flag.Assignments.Select(a => new
        {
            envKey = a.Environment!.Key,
            a.IsEnabled,
            a.PercentageRollout,
            a.RulesJson,
            a.UpdatedAtUtc,
            a.UpdatedBy
        })
    });
}).RequireApiKey("Reader");

flags.MapPost("", async (CreateFlagDto? dto, AppDbContext db, HttpContext ctx, ILogger<Program> logger) =>
{
    try
    {
        if (dto is null)
            return Results.BadRequest(new { message = "Body is required." });

        if (string.IsNullOrWhiteSpace(dto.Key) || string.IsNullOrWhiteSpace(dto.Name))
            return Results.BadRequest(new { message = "Key and Name are required." });

        if (await db.FeatureFlags.AnyAsync(f => f.Key == dto.Key))
        {
            logger.LogWarning("create_flag_conflict key={Key} actor={Actor}", dto.Key, ctx.Items["ApiKeyName"] as string ?? "unknown");
            // Keep returning 409 so clients know it's a conflict; also log it.
            return Results.Conflict(new { message = "Flag key already exists." });
        }

        var flag = new FeatureFlag { Key = dto.Key, Name = dto.Name, Description = dto.Description };
        db.FeatureFlags.Add(flag);
        await db.SaveChangesAsync();

        await AuditAsync(db, ctx, "CreateFlag", "FeatureFlag", flag.Id.ToString(), flag);
        return Results.Created($"/api/flags/{flag.Key}", new { flag.Key, flag.Name, flag.Description });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "create_flag_failed");
        return Results.Problem(title: "Failed to create flag", statusCode: StatusCodes.Status500InternalServerError);
    }
}).RequireApiKey("Admin");

flags.MapPut("/{key}", async (string key, UpdateFlagDto? dto, AppDbContext db, HttpContext ctx, ILogger<Program> logger) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(key))
            return Results.BadRequest(new { message = "Key path parameter is required." });

        var f = await db.FeatureFlags.SingleOrDefaultAsync(x => x.Key == key);
        if (f is null) return Results.NotFound();

        if (dto is not null)
        {
            if (dto.Name is not null) f.Name = dto.Name;
            if (dto.Description is not null) f.Description = dto.Description;
            if (dto.IsArchived.HasValue) f.IsArchived = dto.IsArchived.Value;
        }
        f.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync();
        await AuditAsync(db, ctx, "UpdateFlag", "FeatureFlag", f.Id.ToString(), new { dto });
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "update_flag_failed key={Key}", key);
        return Results.Problem(title: "Failed to update flag", statusCode: StatusCodes.Status500InternalServerError);
    }
}).RequireApiKey("Admin");

flags.MapDelete("/{key}", async (string key, AppDbContext db, HttpContext ctx, ILogger<Program> logger) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(key))
            return Results.BadRequest(new { message = "Key path parameter is required." });

        var f = await db.FeatureFlags.SingleOrDefaultAsync(x => x.Key == key);
        if (f is null) return Results.NotFound();

        db.FeatureFlags.Remove(f);
        await db.SaveChangesAsync();
        await AuditAsync(db, ctx, "DeleteFlag", "FeatureFlag", f.Id.ToString(), new { key });
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "delete_flag_failed key={Key}", key);
        return Results.Problem(title: "Failed to delete flag", statusCode: StatusCodes.Status500InternalServerError);
    }
}).RequireApiKey("Admin");

flags.MapPut("/{key}/assignments/{envKey}", async (
    string key,
    string envKey,
    UpsertAssignmentDto? dto,
    AppDbContext db,
    IFlagEvaluationService evaluator,
    HttpContext ctx,
    ILogger<Program> logger) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(envKey))
            return Results.BadRequest(new { message = "key and envKey are required in the route." });

        if (dto is null)
            return Results.BadRequest(new { message = "Body is required." });

        var flag = await db.FeatureFlags.SingleOrDefaultAsync(f => f.Key == key);
        var env = await db.Environments.SingleOrDefaultAsync(e => e.Key == envKey);
        if (flag is null || env is null) return Results.NotFound();

        var a = await db.FlagAssignments.SingleOrDefaultAsync(x => x.FeatureFlagId == flag.Id && x.EnvironmentId == env.Id);
        var rulesJson = dto.Rules is null ? null : JsonSerializer.Serialize(dto.Rules);

        if (a is null)
        {
            a = new FlagAssignment
            {
                FeatureFlagId = flag.Id,
                EnvironmentId = env.Id,
                IsEnabled = dto.IsEnabled,
                PercentageRollout = dto.PercentageRollout,
                RulesJson = rulesJson,
                UpdatedAtUtc = DateTime.UtcNow,
                UpdatedBy = ctx.Items["ApiKeyName"] as string
            };
            db.FlagAssignments.Add(a);
        }
        else
        {
            a.IsEnabled = dto.IsEnabled;
            a.PercentageRollout = dto.PercentageRollout;
            a.RulesJson = rulesJson;
            a.UpdatedAtUtc = DateTime.UtcNow;
            a.UpdatedBy = ctx.Items["ApiKeyName"] as string;
        }

        await db.SaveChangesAsync();
        await AuditAsync(db, ctx, "UpsertAssignment", "FlagAssignment", a.Id.ToString(), new { key, envKey, dto });

        evaluator.InvalidateCache(key, envKey);
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "upsert_assignment_failed key={Key} env={Env}", key, envKey);
        return Results.Problem(title: "Failed to upsert assignment", statusCode: StatusCodes.Status500InternalServerError);
    }
}).RequireApiKey("Admin");

// Environments & keys & audit
app.MapGet("/api/envs", async (AppDbContext db) =>
{
    var envs = await db.Environments.OrderBy(x => x.Key).Select(x => new { x.Key, x.Name }).ToListAsync();
    return Results.Ok(envs);
}).RequireApiKey("Reader");

app.MapPost("/api/envs", async (CreateEnvDto? dto, AppDbContext db, HttpContext ctx, ILogger<Program> logger) =>
{
    try
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Key) || string.IsNullOrWhiteSpace(dto.Name))
            return Results.BadRequest(new { message = "Key and Name are required." });

        if (await db.Environments.AnyAsync(e => e.Key == dto.Key))
        {
            logger.LogWarning("create_env_conflict key={Key} actor={Actor}", dto.Key, ctx.Items["ApiKeyName"] as string ?? "unknown");
            return Results.Conflict(new { message = "Environment key already exists." });
        }

        db.Environments.Add(new EnvironmentEntity { Key = dto.Key, Name = dto.Name });
        await db.SaveChangesAsync();
        await AuditAsync(db, ctx, "CreateEnv", "Environment", dto.Key, dto);
        return Results.Created($"/api/envs/{dto.Key}", dto);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "create_env_failed key={Key}", dto?.Key);
        return Results.Problem(title: "Failed to create environment", statusCode: StatusCodes.Status500InternalServerError);
    }
}).RequireApiKey("Admin");

app.MapPost("/api/keys", async (CreateApiKeyDto dto, AppDbContext db, HttpContext ctx) =>
{
    if (dto.Role is not ("Admin" or "Reader"))
        return Results.BadRequest(new { message = "Role must be Admin or Reader" });

    var key = new ApiKey { Name = dto.Name, Role = dto.Role, KeyHash = HashUtil.Sha256Hex(dto.PlaintextKey) };
    db.ApiKeys.Add(key);
    await db.SaveChangesAsync();
    await AuditAsync(db, ctx, "CreateApiKey", "ApiKey", key.Id.ToString(), new { dto.Name, dto.Role });
    return Results.Created($"/api/keys/{key.Id}", new { key.Id, key.Name, key.Role });
}).RequireApiKey("Admin");

app.MapGet("/api/audit", async (int take, AppDbContext db) =>
{
    take = Math.Clamp(take <= 0 ? 100 : take, 1, 500);
    var items = await db.AuditLogs.OrderByDescending(x => x.TimestampUtc).Take(take).ToListAsync();
    return Results.Ok(items);
}).RequireApiKey("Admin");

app.MapGet("/", () => Results.Redirect("/admin/"));

app.Run();

static bool IsConnectivityError(Exception ex)
{
    return ex switch
    {
        SqlException => true,
        InvalidOperationException { InnerException: SqlException } => true,
        _ => false
    };
}

static async Task AuditAsync(AppDbContext db, HttpContext ctx, string action, string entityType, string entityId, object payload)
{
    db.AuditLogs.Add(new AuditLog
    {
        Actor = ctx.Items["ApiKeyName"] as string ?? "system",
        Action = action,
        EntityType = entityType,
        EntityId = entityId,
        DataJson = JsonSerializer.Serialize(payload),
        TimestampUtc = DateTime.UtcNow
    });
    await db.SaveChangesAsync();
}
