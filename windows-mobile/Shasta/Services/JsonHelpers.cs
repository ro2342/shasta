using Windows.Data.Json;

namespace Shasta.Services
{
    // Shared defensive JSON readers — every field read this way tolerates a
    // missing key or a type mismatch (e.g. an older server response, or a
    // field ABS added/renamed) by falling back instead of throwing. See
    // wp-apps/04-convencoes-de-codigo.md's JSON-parsing convention.
    public static class JsonHelpers
    {
        public static string GetStringOrDefault(JsonObject obj, string key, string fallback = "")
        {
            if (obj != null && obj.ContainsKey(key) && obj[key].ValueType == JsonValueType.String)
            {
                return obj[key].GetString();
            }
            return fallback;
        }

        public static string GetStringOrNull(JsonObject obj, string key)
        {
            if (obj != null && obj.ContainsKey(key) && obj[key].ValueType == JsonValueType.String)
            {
                return obj[key].GetString();
            }
            return null;
        }

        public static double GetDoubleOrDefault(JsonObject obj, string key, double fallback = 0)
        {
            if (obj != null && obj.ContainsKey(key) && obj[key].ValueType == JsonValueType.Number)
            {
                return obj[key].GetNumber();
            }
            return fallback;
        }

        public static long GetInt64OrDefault(JsonObject obj, string key, long fallback = 0)
        {
            if (obj != null && obj.ContainsKey(key) && obj[key].ValueType == JsonValueType.Number)
            {
                return (long)obj[key].GetNumber();
            }
            return fallback;
        }

        public static bool GetBoolOrDefault(JsonObject obj, string key, bool fallback = false)
        {
            if (obj != null && obj.ContainsKey(key) && obj[key].ValueType == JsonValueType.Boolean)
            {
                return obj[key].GetBoolean();
            }
            return fallback;
        }

        public static JsonObject GetObjectOrNull(JsonObject obj, string key)
        {
            if (obj != null && obj.ContainsKey(key) && obj[key].ValueType == JsonValueType.Object)
            {
                return obj[key].GetObject();
            }
            return null;
        }

        public static JsonArray GetArrayOrEmpty(JsonObject obj, string key)
        {
            if (obj != null && obj.ContainsKey(key) && obj[key].ValueType == JsonValueType.Array)
            {
                return obj[key].GetArray();
            }
            return new JsonArray();
        }
    }
}
