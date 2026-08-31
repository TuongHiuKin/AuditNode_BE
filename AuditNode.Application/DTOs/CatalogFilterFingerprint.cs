using System.Security.Cryptography;
using System.Text;

namespace AuditNode.Application.DTOs;

public static class CatalogFilterFingerprint
{
    public static string None { get; } = Hash("none");

    public static string Resources(string? ownerUserId, string? labelKey = null, string? labelValue = null) =>
        Hash($"ownerUserId={NormalizeExact(ownerUserId)}\nlabelKey={NormalizeExact(labelKey)}\nlabelValue={NormalizeExact(labelValue)}");

    public static string Applications(string? labelKey, string? labelValue) =>
        Resources(null, labelKey, labelValue);

    public static string Search(string keyword, string? ownerUserId = null, string? labelKey = null, string? labelValue = null) =>
        Hash($"q={NormalizeSearch(keyword)}\nownerUserId={NormalizeExact(ownerUserId)}\nlabelKey={NormalizeExact(labelKey)}\nlabelValue={NormalizeExact(labelValue)}");

    private static string NormalizeExact(string? value) => value?.Trim() ?? string.Empty;
    private static string NormalizeSearch(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
