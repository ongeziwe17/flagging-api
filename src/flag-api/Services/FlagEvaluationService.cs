using FeatureFlags.Api.Data;
using FeatureFlags.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FeatureFlags.Api.Services;

public interface IFlagEvaluationService
{
    Task<EvalResponse> EvaluateAsync(string flagKey, string envKey, string userId, Dictionary<string, string>? attributes);
    void InvalidateCache(string flagKey, string envKey);
}

public class FlagEvaluationService : IFlagEvaluationService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;              // L1
    private readonly IDistributedCache _dcache;        // L2 (Redis)
    private readonly ILogger<FlagEvaluationService> _logger;

    // metrics
    private static readonly Meter s_meter = new("FeatureFlags.Api", "1.0.0");
    private static readonly Counter<long> s_evalCounter = s_meter.CreateCounter<long>("feature_flag_evaluations_total");
    private static readonly Counter<long> s_l1HitCounter = s_meter.CreateCounter<long>("feature_flag_cache_l1_hits_total");
    private static readonly Counter<long> s_l1MissCounter = s_meter.CreateCounter<long>("feature_flag_cache_l1_misses_total");
    private static readonly Counter<long> s_l2HitCounter = s_meter.CreateCounter<long>("feature_flag_cache_l2_hits_total");
    private static readonly Counter<long> s_l2MissCounter = s_meter.CreateCounter<long>("feature_flag_cache_l2_misses_total");

    // object we cache
    private record CachedAssign(bool IsEnabled, int? Percentage, string? RulesJson);

    public FlagEvaluationService(AppDbContext db,
                                 IMemoryCache cache,
                                 IDistributedCache dcache,
                                 ILogger<FlagEvaluationService> logger)
    {
        _db = db;
        _cache = cache;
        _dcache = dcache;
        _logger = logger;
    }

    public void InvalidateCache(string flagKey, string envKey)
    {
        var key = CacheKey(flagKey, envKey);
        _cache.Remove(key);
        _dcache.Remove(key);
        _logger.LogInformation("cache_invalidated flag={Flag} env={Env}", flagKey, envKey);
    }

    public async Task<EvalResponse> EvaluateAsync(string flagKey, string envKey, string userId, Dictionary<string, string>? attributes)
    {
        var key = CacheKey(flagKey, envKey);

        // ---- L1: try memory cache
        if (!_cache.TryGetValue(key, out CachedAssign? assign))
        {
            s_l1MissCounter.Add(1);

            // ---- L2: try distributed cache (Redis)
            var blob = _dcache.GetString(key); // sync extension, fine here
            if (!string.IsNullOrWhiteSpace(blob))
            {
                try
                {
                    assign = JsonSerializer.Deserialize<CachedAssign>(blob);
                    if (assign is not null)
                    {
                        s_l2HitCounter.Add(1);
                        _cache.Set(key, assign, new MemoryCacheEntryOptions
                        {
                            SlidingExpiration = TimeSpan.FromSeconds(30)
                        });
                    }
                    else
                    {
                        s_l2MissCounter.Add(1);
                    }
                }
                catch
                {
                    s_l2MissCounter.Add(1);
                }
            }
            else
            {
                s_l2MissCounter.Add(1);
            }

            // ---- DB fallback
            if (assign is null)
            {
                var a = await _db.FlagAssignments
                    .Include(x => x.Environment)
                    .Include(x => x.FeatureFlag)
                    .Where(x => x.FeatureFlag!.Key == flagKey && x.Environment!.Key == envKey && !x.FeatureFlag!.IsArchived)
                    .SingleOrDefaultAsync();

                assign = (a is null)
                    ? new CachedAssign(false, null, null)
                    : new CachedAssign(a.IsEnabled, a.PercentageRollout, a.RulesJson);

                // write-through to both caches
                _cache.Set(key, assign, new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromSeconds(30)
                });
                _dcache.SetString(key, JsonSerializer.Serialize(assign), new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
                });
            }
        }
        else
        {
            s_l1HitCounter.Add(1);
        }

        // ---- Evaluate
        EvalResponse result;
        if (!assign!.IsEnabled)
        {
            var (rulesPassed, matchedRule) = EvaluateRules(assign.RulesJson, userId, attributes);
            if (rulesPassed)
            {
                result = new EvalResponse(true, "matched_rule", matchedRule);
            }
            else if (assign.Percentage.HasValue)
            {
                int bucket = HashUtil.BucketPercent(flagKey, userId);
                result = (bucket < assign.Percentage.Value)
                    ? new EvalResponse(true, $"percentage_{assign.Percentage.Value}", null)
                    : new EvalResponse(false, "disabled", null);
            }
            else
            {
                result = new EvalResponse(false, "disabled", null);
            }
        }
        else
        {
            result = new EvalResponse(true, "enabled", null);
        }

        // telemetry
        s_evalCounter.Add(1);
        _logger.LogInformation("flag_eval flag={Flag} env={Env} user={User} enabled={Enabled} reason={Reason}",
            flagKey, envKey, userId, result.Enabled, result.Reason);

        return result;
    }

    private static (bool passed, string? matchedRule) EvaluateRules(string? rulesJson, string userId, Dictionary<string, string>? attrs)
    {
        var rs = RuleSetParser.TryParse(rulesJson);
        if (rs == null || rs.Rules.Count == 0) return (false, null);

        var bag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["userId"] = userId };
        if (attrs != null) foreach (var kv in attrs) bag[kv.Key] = kv.Value;

        static bool TryNum(string s, out double d)
            => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d);

        bool Evaluate(Rule r)
        {
            bag.TryGetValue(r.Attribute, out var actual);
            actual ??= string.Empty;
            var target = r.Value ?? string.Empty;

            return r.Operator switch
            {
                "eq" => string.Equals(actual, target, StringComparison.OrdinalIgnoreCase),
                "ne" => !string.Equals(actual, target, StringComparison.OrdinalIgnoreCase),
                "contains" => actual.Contains(target, StringComparison.OrdinalIgnoreCase),
                "startsWith" => actual.StartsWith(target, StringComparison.OrdinalIgnoreCase),
                "endsWith" => actual.EndsWith(target, StringComparison.OrdinalIgnoreCase),
                "in" => target.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                              .Any(x => string.Equals(x, actual, StringComparison.OrdinalIgnoreCase)),
                "gt" => TryNum(actual, out var a1) && TryNum(target, out var t1) && a1 > t1,
                "gte" => TryNum(actual, out var a2) && TryNum(target, out var t2) && a2 >= t2,
                "lt" => TryNum(actual, out var a3) && TryNum(target, out var t3) && a3 < t3,
                "lte" => TryNum(actual, out var a4) && TryNum(target, out var t4) && a4 <= t4,
                "regex" => SafeRegex(actual, target),
                _ => false
            };

            static bool SafeRegex(string actual, string pattern)
            {
                try { return Regex.IsMatch(actual, pattern, RegexOptions.IgnoreCase); }
                catch { return false; }
            }
        }

        if (rs.Match.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var r in rs.Rules)
                if (Evaluate(r)) return (true, JsonSerializer.Serialize(r));
            return (false, null);
        }
        else // "all"
        {
            foreach (var r in rs.Rules)
                if (!Evaluate(r)) return (false, null);
            return (true, JsonSerializer.Serialize(rs.Rules));
        }
    }

    private static string CacheKey(string flagKey, string envKey) => $"flag:{flagKey}|env:{envKey}";
}