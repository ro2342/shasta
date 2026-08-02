using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Shasta.Models;
using Windows.Data.Json;

namespace Shasta.Services
{
    public sealed class AbsAuthResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public AbsSession Session { get; set; }
    }

    // Owns the login/refresh/logout flow against an Audiobookshelf server.
    // AbsApiClient is (re)configured as a side effect of every method here,
    // so callers never have to remember to wire the two together.
    public static class AbsAuthService
    {
        private static AbsSession _currentSession;
        private static Task<bool> _refreshTask;
        private static readonly object _refreshGate = new object();

        public static AbsSession CurrentSession => _currentSession;

        public static async Task<AbsAuthResult> LoginAsync(string serverUrl, string username, string password)
        {
            string normalizedServerUrl = NormalizeServerUrl(serverUrl);
            try
            {
                JsonObject body = new JsonObject
                {
                    ["username"] = JsonValue.CreateStringValue(username ?? ""),
                    ["password"] = JsonValue.CreateStringValue(password ?? ""),
                };
                // Required or the refresh token is only set as an httpOnly
                // cookie this client can never read.
                Dictionary<string, string> headers = new Dictionary<string, string> { ["x-return-tokens"] = "true" };

                AbsApiClient.Configure(normalizedServerUrl, "");
                JsonObject response = await AbsApiClient.PostJsonAsync("/login", body, headers);

                JsonObject userObj = JsonHelpers.GetObjectOrNull(response, "user");
                if (userObj == null)
                {
                    return new AbsAuthResult { Success = false, ErrorMessage = "Unexpected response from server." };
                }

                AbsSession session = BuildSessionFromLoginResponse(normalizedServerUrl, userObj);
                ApplySession(session);
                return new AbsAuthResult { Success = true, Session = session };
            }
            catch (AbsApiException ex)
            {
                return new AbsAuthResult { Success = false, ErrorMessage = FriendlyError(ex) };
            }
            catch (Exception ex)
            {
                return new AbsAuthResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        // ASSUMPTION, not confirmed from source (see the plan's open
        // risks): treats a user-generated API key as a bearer token used
        // directly, validated by calling GET /api/me. If a live server
        // turns out to expose a dedicated /api/authorize endpoint instead,
        // this is the method to change — verify in Phase 3.
        public static async Task<AbsAuthResult> LoginWithApiKeyAsync(string serverUrl, string apiKey)
        {
            string normalizedServerUrl = NormalizeServerUrl(serverUrl);
            try
            {
                AbsApiClient.Configure(normalizedServerUrl, apiKey);
                JsonObject me = await AbsApiClient.GetJsonAsync("/api/me");

                AbsSession session = new AbsSession
                {
                    ServerUrl = normalizedServerUrl,
                    LegacyToken = apiKey,
                    IsLegacyMode = true,
                    User = AbsUser.Parse(me),
                };
                ApplySession(session);
                return new AbsAuthResult { Success = true, Session = session };
            }
            catch (AbsApiException ex)
            {
                return new AbsAuthResult { Success = false, ErrorMessage = FriendlyError(ex) };
            }
            catch (Exception ex)
            {
                return new AbsAuthResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        // Single-flight: concurrent callers all await the same in-flight
        // refresh instead of racing multiple rotations of the refresh
        // token against each other.
        public static Task<bool> TryRefreshAsync()
        {
            lock (_refreshGate)
            {
                if (_refreshTask == null || _refreshTask.IsCompleted)
                {
                    _refreshTask = DoRefreshAsync();
                }
                return _refreshTask;
            }
        }

        private static async Task<bool> DoRefreshAsync()
        {
            AbsSession session = _currentSession;
            if (session == null || !session.CanRefresh)
            {
                return false;
            }

            try
            {
                Dictionary<string, string> headers = new Dictionary<string, string> { ["x-refresh-token"] = session.RefreshToken };
                JsonObject response = await AbsApiClient.PostJsonAsync("/auth/refresh", null, headers);
                JsonObject userObj = JsonHelpers.GetObjectOrNull(response, "user");
                if (userObj == null)
                {
                    return false;
                }

                AbsSession refreshed = BuildSessionFromLoginResponse(session.ServerUrl, userObj);
                ApplySession(refreshed);
                return true;
            }
            catch (AbsApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // Server predates ABS 2.26 (no refresh route) — or a
                // reverse proxy is only forwarding /api and swallowed this
                // root-level route. Either way, refreshing isn't possible;
                // the caller falls back to forcing a re-login.
                return false;
            }
            catch (AbsApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized || ex.StatusCode == HttpStatusCode.Forbidden)
            {
                // The refresh token itself was rejected — session is
                // unrecoverable, must re-login.
                await LogoutAsync();
                return false;
            }
            catch
            {
                return false;
            }
        }

        public static Task LogoutAsync()
        {
            _currentSession = null;
            AbsSessionStore.Clear();
            AbsApiClient.Configure("", "");
            return Task.CompletedTask;
        }

        // Called once at app boot. Returns true if a usable session is now
        // configured on AbsApiClient (fresh or freshly refreshed).
        public static async Task<bool> RestoreSessionAsync()
        {
            AbsSession session = AbsSessionStore.Load();
            if (session == null || string.IsNullOrEmpty(session.BearerToken))
            {
                return false;
            }

            _currentSession = session;
            AbsApiClient.Configure(session.ServerUrl, session.BearerToken);

            if (session.NeedsRefresh)
            {
                return await TryRefreshAsync();
            }
            return true;
        }

        private static AbsSession BuildSessionFromLoginResponse(string serverUrl, JsonObject userObj)
        {
            string accessToken = JsonHelpers.GetStringOrDefault(userObj, "accessToken");
            string refreshToken = JsonHelpers.GetStringOrDefault(userObj, "refreshToken");
            string legacyToken = JsonHelpers.GetStringOrDefault(userObj, "token");

            bool isLegacy = string.IsNullOrEmpty(accessToken) && string.IsNullOrEmpty(refreshToken);

            return new AbsSession
            {
                ServerUrl = serverUrl,
                User = AbsUser.Parse(userObj),
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                LegacyToken = legacyToken,
                IsLegacyMode = isLegacy,
                // Confirmed 1-hour access-token TTL — refreshed proactively
                // via AbsSession.NeedsRefresh's 1-minute buffer.
                AccessTokenExpiresAtUtc = isLegacy ? (DateTimeOffset?)null : DateTimeOffset.UtcNow.AddHours(1),
            };
        }

        private static void ApplySession(AbsSession session)
        {
            _currentSession = session;
            AbsSessionStore.Save(session);
            AbsApiClient.Configure(session.ServerUrl, session.BearerToken);
        }

        private static string NormalizeServerUrl(string serverUrl)
        {
            return (serverUrl ?? "").Trim().TrimEnd('/');
        }

        private static string FriendlyError(AbsApiException ex)
        {
            switch (ex.StatusCode)
            {
                case HttpStatusCode.Unauthorized:
                    return "Incorrect username, password, or API key.";
                case HttpStatusCode.NotFound:
                    return "Server not found at that address.";
                default:
                    return ex.Message;
            }
        }
    }
}
