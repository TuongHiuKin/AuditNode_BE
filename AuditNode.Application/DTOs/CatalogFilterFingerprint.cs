using System.Security.Cryptography;
using System.Text;

namespace AuditNode.Application.DTOs;

public static class CatalogFilterFingerprint
{
    public static string None { get; } = Hash("none");

    public static string Applications(string? labelKey, string? labelValue) =>
        Hash($"labelKey={NormalizeExact(labelKey)}\nlabelValue={NormalizeExact(labelValue)}");

    public static string Search(string keyword) => Hash($"q={NormalizeSearch(keyword)}");

    private static string NormalizeExact(string? value) => value?.Trim() ?? string.Empty;
    private static string NormalizeSearch(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
