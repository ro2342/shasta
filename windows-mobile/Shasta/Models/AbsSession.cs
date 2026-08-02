using System;
using Shasta.Services;
using Windows.Data.Json;

namespace Shasta.Models
{
    // The full logged-in state persisted to PasswordVault by
    // AbsSessionStore (Phase 2) — server URL + user + whichever token
    // scheme the server supports. ABS servers pre-2.26 return only a
    // legacy `user.token` (no refresh); 2.26+ returns AccessToken/
    // RefreshToken that rotate on refresh.
    public sealed class AbsSession
    {
        public string ServerUrl { get; set; } = "";
        public AbsUser User { get; set; }
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public string LegacyToken { get; set; } = "";
        public bool IsLegacyMode { get; set; }
        public DateTimeOffset? AccessTokenExpiresAtUtc { get; set; }

        // The token AbsApiClient should send as the bearer credential,
        // regardless of which auth mode this session is in.
        public string BearerToken => IsLegacyMode ? LegacyToken : AccessToken;

        public bool CanRefresh => !IsLegacyMode && !string.IsNullOrEmpty(RefreshToken);

        // 1-minute buffer before the confirmed 1-hour access-token TTL, so a
        // request never races an expiry that happens mid-flight.
        public bool NeedsRefresh =>
            CanRefresh && AccessTokenExpiresAtUtc.HasValue &&
            DateTimeOffset.UtcNow >= AccessTokenExpiresAtUtc.Value.AddMinutes(-1);

        public JsonObject ToJson()
        {
            JsonObject obj = new JsonObject
            {
                ["serverUrl"] = JsonValue.CreateStringValue(ServerUrl ?? ""),
                ["accessToken"] = JsonValue.CreateStringValue(AccessToken ?? ""),
                ["refreshToken"] = JsonValue.CreateStringValue(RefreshToken ?? ""),
                ["legacyToken"] = JsonValue.CreateStringValue(LegacyToken ?? ""),
                ["isLegacyMode"] = JsonValue.CreateBooleanValue(IsLegacyMode),
                ["accessTokenExpiresAtUtc"] = JsonValue.CreateStringValue(
                    AccessTokenExpiresAtUtc.HasValue ? AccessTokenExpiresAtUtc.Value.ToString("o") : ""),
            };
            if (User != null)
            {
                obj["user"] = new JsonObject
                {
                    ["id"] = JsonValue.CreateStringValue(User.Id ?? ""),
                    ["username"] = JsonValue.CreateStringValue(User.Username ?? ""),
                    ["email"] = JsonValue.CreateStringValue(User.Email ?? ""),
                    ["isActive"] = JsonValue.CreateBooleanValue(User.IsActive),
                    ["type"] = JsonValue.CreateStringValue(User.Type ?? ""),
                };
            }
            return obj;
        }

        public static AbsSession Parse(JsonObject obj)
        {
            AbsSession session = new AbsSession
            {
                ServerUrl = JsonHelpers.GetStringOrDefault(obj, "serverUrl"),
                AccessToken = JsonHelpers.GetStringOrDefault(obj, "accessToken"),
                RefreshToken = JsonHelpers.GetStringOrDefault(obj, "refreshToken"),
                LegacyToken = JsonHelpers.GetStringOrDefault(obj, "legacyToken"),
                IsLegacyMode = JsonHelpers.GetBoolOrDefault(obj, "isLegacyMode"),
            };

            string expiresStr = JsonHelpers.GetStringOrNull(obj, "accessTokenExpiresAtUtc");
            if (!string.IsNullOrEmpty(expiresStr) && DateTimeOffset.TryParse(expiresStr, out DateTimeOffset expires))
            {
                session.AccessTokenExpiresAtUtc = expires;
            }

            JsonObject userObj = JsonHelpers.GetObjectOrNull(obj, "user");
            if (userObj != null)
            {
                session.User = AbsUser.Parse(userObj);
            }

            return session;
        }
    }
}
