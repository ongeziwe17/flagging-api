using System.Security.Cryptography;
using System.Text;

namespace FeatureFlags.Api.Services;

public static class HashUtil
{
    public static string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // Deterministic 0..99 bucket from (flagKey, userId)
    public static int BucketPercent(string flagKey, string userId)
    {
        var input = $"{flagKey}::{userId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        // take first two bytes as number 0..65535, mod 100
        int n = (hash[0] << 8) + hash[1];
        return n % 100;
    }
}
