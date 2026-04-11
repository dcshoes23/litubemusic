using System;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Plugin.Litubemusic.ImportLists.YouTubeMusic
{
    public class YouTubeMusicSettingsBaseValidator<TSettings> : AbstractValidator<TSettings>
        where TSettings : YouTubeMusicSettingsBase<TSettings>
    {
        public YouTubeMusicSettingsBaseValidator()
        {
            RuleFor(s => s.ClientId).NotEmpty()
                .WithMessage("Google Cloud Client ID is required");

            RuleFor(s => s.ClientSecret).NotEmpty()
                .WithMessage("Google Cloud Client Secret is required");

            RuleFor(s => s.AccessToken).NotEmpty()
                .WithMessage("Not authenticated. Click 'Authenticate with YouTube Music'.");

            RuleFor(s => s.RefreshToken).NotEmpty()
                .WithMessage("No refresh token. Re-authenticate to obtain one.");
        }
    }

    /// <summary>
    /// Base settings for all YouTube Music import lists.
    /// Holds Google OAuth 2.0 credentials and token state.
    /// </summary>
    public abstract class YouTubeMusicSettingsBase<TSettings> : IImportListSettings
        where TSettings : YouTubeMusicSettingsBase<TSettings>
    {
        // Unused by this plugin but required by IImportListSettings.
        public string BaseUrl { get; set; } = string.Empty;

        protected YouTubeMusicSettingsBase()
        {
            SignIn = "startOAuth";
        }

        // ── OAuth endpoints (fixed) ───────────────────────────────

        public string OAuthUrl => "https://accounts.google.com/o/oauth2/v2/auth";
        public string TokenUrl => "https://oauth2.googleapis.com/token";
        public string Scope => "https://www.googleapis.com/auth/youtube.readonly";

        /// <summary>
        /// The redirect URI Google will send the authorization code to.
        /// Must be registered in the Google Cloud Console OAuth credential.
        ///
        /// Lidarr's built-in oauth.html page is used as the landing page so that
        /// its JavaScript can extract the authorization code from the URL and hand
        /// it back to our RequestAction("getOAuthToken") handler on the server.
        /// </summary>
        public string RedirectUri => $"{LidarrUrl.TrimEnd('/')}/oauth.html";

        // ── User-visible settings ─────────────────────────────────

        [FieldDefinition(0,
            Label = "Google Cloud Client ID",
            HelpText = "OAuth 2.0 Client ID from Google Cloud Console → APIs & Services → Credentials. Use a 'Web application' credential type.",
            HelpLink = "https://github.com/dcshoes23/litubemusic#google-cloud-setup",
            Privacy = PrivacyLevel.ApiKey)]
        public string ClientId { get; set; } = string.Empty;

        [FieldDefinition(1,
            Label = "Google Cloud Client Secret",
            Type = FieldType.Password,
            HelpText = "OAuth 2.0 Client Secret paired with the Client ID above.",
            Privacy = PrivacyLevel.Password)]
        public string ClientSecret { get; set; } = string.Empty;

        [FieldDefinition(2,
            Label = "Lidarr Base URL",
            HelpText = "The URL you use to reach Lidarr (e.g. http://localhost:8686). Must also be registered as an Authorized Redirect URI in Google Cloud Console as: {LidarrBaseURL}/oauth.html")]
        public string LidarrUrl { get; set; } = "http://localhost:8686";

        // ── Hidden OAuth token storage ────────────────────────────

        [FieldDefinition(10, Label = "Access Token", Hidden = HiddenType.Hidden)]
        public string AccessToken { get; set; } = string.Empty;

        [FieldDefinition(11, Label = "Refresh Token", Hidden = HiddenType.Hidden)]
        public string RefreshToken { get; set; } = string.Empty;

        [FieldDefinition(12, Label = "Token Expires", Hidden = HiddenType.Hidden)]
        public DateTime Expires { get; set; }

        // ── OAuth button ──────────────────────────────────────────

        [FieldDefinition(98,
            Label = "Authenticate with YouTube Music",
            Type = FieldType.OAuth)]
        public string SignIn { get; set; }

        // ── Validation ────────────────────────────────────────────

        protected virtual AbstractValidator<TSettings> CreateValidator() =>
            new YouTubeMusicSettingsBaseValidator<TSettings>();

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(CreateValidator().Validate((TSettings)this));
        }
    }
}
