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
            return data != null && data.TryGetValue("productId", out var val) ? val : null;
        }
    }
}
