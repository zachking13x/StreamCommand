using System.IO;
using System.Text.Json;

namespace StreamCommand.Services
{
    public static class LocalCache
    {
        private static readonly string FilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StreamCommand", "subscription.json");

        public static void SaveProState(string? productId)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            var data = new { productId };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(data));
        }

        public static string? LoadProState()
        {
            if (!File.Exists(FilePath))
                return null;

            var json = File.ReadAllText(FilePath);
            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

            // Use TryGetValue so a missing or null "productId" key never throws KeyNotFoundException.
            // SECURITY 4 note: this file has no HMAC/integrity check. MainWindow validates the returned
            // value against _validProductIds and only sets IsProPending (NOT IsPro) from this cache.
            // IsPro is only set after Microsoft Store confirms the license via EntitlementService.RefreshAsync().
            // No view must gate on IsProPending — it must not be used for access-control decisions.
            return data != null && data.TryGetValue("productId", out var val) ? val : null;
        }
    }
}
