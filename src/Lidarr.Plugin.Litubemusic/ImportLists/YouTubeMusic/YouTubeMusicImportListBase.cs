using System;
using System.Collections.Generic;
using System.Net;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Validation;

namespace NzbDrone.Plugin.Litubemusic.ImportLists.YouTubeMusic
{
    /// <summary>
    /// Abstract base for all YouTube Music import lists.
    /// Handles Google OAuth 2.0 (Authorization Code flow) and token lifecycle.
    ///
    /// OAuth flow overview
    /// ───────────────────
    ///  1. User enters Client ID + Secret + Lidarr Base URL in settings.
    ///  2. User clicks "Authenticate with YouTube Music" (FieldType.OAuth).
    ///  3. Lidarr frontend calls RequestAction("startOAuth") → receives { OauthUrl }.
    ///  4. Lidarr opens the Google consent page in a popup.
    ///  5. After consent, Google redirects to {LidarrUrl}/oauth.html?code=XXX.
    ///  6. Lidarr's oauth.html JavaScript reads the ?code= query param and calls
    ///     RequestAction("getOAuthToken", { code: "XXX" }) on the server.
    ///  7. getOAuthToken exchanges the code for access + refresh tokens via the proxy.
    ///  8. Returns { accessToken, refreshToken, expires } so the frontend can store them.
    ///
    /// Note: step 6 assumes Lidarr's oauth.html passes query parameters (not just
    /// hash fragments) back to the parent. If your Lidarr build only reads hash params,
    /// register {LidarrUrl}/api/v1/litubemusic/callback as the redirect URI instead and
    /// add a thin controller that exchanges the code and redirects to oauth.html with
    /// hash params.
    /// </summary>
    public abstract class YouTubeMusicImportListBase<TSettings>
        : ImportListBase<TSettings>
        where TSettings : YouTubeMusicSettingsBase<TSettings>, new()
    {
        protected readonly IYouTubeMusicProxy _proxy;
        private readonly IImportListRepository _importListRepository;

        protected YouTubeMusicImportListBase(
            IYouTubeMusicProxy proxy,
            IImportListRepository importListRepository,
            IImportListStatusService importListStatusService,
            IConfigService configService,
            IParsingService parsingService,
            Logger logger)
            : base(importListStatusService, configService, parsingService, logger)
        {
            _proxy = proxy;
            _importListRepository = importListRepository;
        }

        public override ImportListType ListType => ImportListType.Other;
        public override TimeSpan MinRefreshInterval => TimeSpan.FromHours(12);

        // ── Token management ──────────────────────────────────────

        /// <summary>
        /// Returns a valid access token, transparently refreshing it when needed.
        /// </summary>
        protected string GetAccessToken()
        {
            // Refresh proactively if the token expires within the next 5 minutes
            if (Settings.Expires < DateTime.UtcNow.AddMinutes(5))
            {
                DoRefreshToken();
            }

            return Settings.AccessToken;
        }

        /// <summary>
        /// Exchanges the stored refresh token for a new access token and persists
        /// the updated Settings so the new token survives restarts.
        /// </summary>
        protected void DoRefreshToken()
        {
            _logger.Trace("[Litubemusic] Refreshing YouTube access token");

            if (Settings.RefreshToken.IsNullOrWhiteSpace())
            {
                throw new InvalidOperationException(
                    "No refresh token available. Please re-authenticate with YouTube Music.");
            }

            var token = _proxy.RefreshAccessToken(
                Settings.ClientId,
                Settings.ClientSecret,
                Settings.RefreshToken,
                Settings.TokenUrl);

            if (token == null || token.AccessToken.IsNullOrWhiteSpace())
            {
                throw new InvalidOperationException(
                    "YouTube token refresh returned an empty response.");
            }

            Settings.AccessToken = token.AccessToken;
            Settings.Expires = DateTime.UtcNow.AddSeconds(token.ExpiresIn);

            // Google only issues a new refresh token on the first authorization or
            // when explicitly requested. Preserve the existing one if none was returned.
            if (token.RefreshToken.IsNotNullOrWhiteSpace())
            {
                Settings.RefreshToken = token.RefreshToken;
            }

            PersistSettings();
        }

        /// <summary>Saves updated Settings to the database.</summary>
        protected void PersistSettings()
        {
            if (Definition?.Id > 0)
            {
                _importListRepository.UpdateSettings((ImportListDefinition)Definition);
            }
        }

        // ── RequestAction — called by the Lidarr frontend ─────────

        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            switch (action)
            {
                case "startOAuth":
                    return HandleStartOAuth(query);

                case "getOAuthToken":
                    return HandleGetOAuthToken(query);

                default:
                    return new { };
            }
        }

        private object HandleStartOAuth(IDictionary<string, string> query)
        {
            if (Settings.ClientId.IsNullOrWhiteSpace() || Settings.ClientSecret.IsNullOrWhiteSpace())
            {
                return new
                {
                    error = "Enter your Google Cloud Client ID and Client Secret before authenticating."
                };
            }

            // Build Google's authorization URL.
            // access_type=offline  → request a refresh token
            // prompt=consent       → always show the consent screen so a refresh token is issued
            // state                → passed through by Google; Lidarr uses it as the callback URL
            var stateParam = query.GetValueOrDefault("callbackUrl", string.Empty);

            var authUrlBuilder = new HttpRequestBuilder(Settings.OAuthUrl)
                .AddQueryParam("client_id", Settings.ClientId)
                .AddQueryParam("response_type", "code")
                .AddQueryParam("redirect_uri", Settings.RedirectUri)
                .AddQueryParam("scope", Settings.Scope)
                .AddQueryParam("access_type", "offline")
                .AddQueryParam("prompt", "consent")
                .AddQueryParam("state", stateParam);

            var authUrl = authUrlBuilder.Build().Url.ToString();

            _logger.Debug("[Litubemusic] startOAuth → {0}", authUrl);
            return new { OauthUrl = authUrl };
        }

        private object HandleGetOAuthToken(IDictionary<string, string> query)
        {
            // Lidarr's oauth.html passes back whatever parameters it received from the
            // redirect URI. For our Authorization Code flow that is ?code=XXX.
            // We exchange the code server-side so the client_secret never leaves the server.
            var code = query.GetValueOrDefault("code");

            if (code.IsNullOrWhiteSpace())
            {
                _logger.Warn("[Litubemusic] getOAuthToken called without 'code' parameter. " +
                    "Query keys: {0}", string.Join(", ", query.Keys));
                return new
                {
                    error = "No authorization code received. Make sure oauth.html is configured as the redirect URI."
                };
            }

            try
            {
                _logger.Debug("[Litubemusic] Exchanging authorization code for tokens");

                var token = _proxy.ExchangeCode(
                    Settings.ClientId,
                    Settings.ClientSecret,
                    Settings.RedirectUri,
                    code,
                    Settings.TokenUrl);

                if (token == null || token.AccessToken.IsNullOrWhiteSpace())
                {
                    return new { error = "Google returned an empty token response." };
                }

                var expires = DateTime.UtcNow.AddSeconds(token.ExpiresIn);

                // Return the token data to the frontend. Lidarr stores these values into
                // the matching Settings fields (AccessToken, RefreshToken, Expires).
                return new
                {
                    accessToken = token.AccessToken,
                    refreshToken = token.RefreshToken,
                    expires = expires.ToString("o")
                };
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "[Litubemusic] Failed to exchange OAuth code");
                return new { error = ex.Message };
            }
        }

        // ── Connection test ───────────────────────────────────────

        protected override void Test(List<ValidationFailure> failures)
        {
            failures.AddIfNotNull(TestConnection());
        }

        private ValidationFailure? TestConnection()
        {
            if (Settings.AccessToken.IsNullOrWhiteSpace())
            {
                return new ValidationFailure(string.Empty,
                    "Not authenticated. Click 'Authenticate with YouTube Music' and complete the sign-in flow.");
            }

            try
            {
                var token = GetAccessToken();
                var playlists = _proxy.GetUserPlaylists(token);

                _logger.Debug("[Litubemusic] Connection test OK — {0} playlist(s) found.",
                    playlists?.Count ?? 0);

                return null;
            }
            catch (HttpException ex) when (ex.Response?.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new ValidationFailure(string.Empty,
                    "YouTube authentication failed (401). Re-authenticate to get a fresh token.");
            }
            catch (HttpException ex) when (ex.Response?.StatusCode == HttpStatusCode.Forbidden)
            {
                return new ValidationFailure(string.Empty,
                    "YouTube API access denied (403). Ensure the YouTube Data API v3 is enabled in your Google Cloud project.");
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "[Litubemusic] Connection test failed");
                return new ValidationFailure(string.Empty,
                    $"Unable to connect to YouTube Music: {ex.Message}");
            }
        }
    }
}
