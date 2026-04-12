using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;

namespace NzbDrone.Plugin.Litubemusic.ImportLists.YouTubeMusic
{
    // ──────────────────────────────────────────────────────────────
    // Token response from https://oauth2.googleapis.com/token
    // ──────────────────────────────────────────────────────────────
    public class YouTubeTokenResponse
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;

        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonProperty("token_type")]
        public string TokenType { get; set; } = string.Empty;
    }

    // ──────────────────────────────────────────────────────────────
    // Playlist list response (GET /youtube/v3/playlists)
    // ──────────────────────────────────────────────────────────────
    public class YouTubePlaylistSnippet
    {
        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;

        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;
    }

    public class YouTubePlaylist
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("snippet")]
        public YouTubePlaylistSnippet Snippet { get; set; } = new();
    }

    public class YouTubePlaylistListResponse
    {
        [JsonProperty("nextPageToken")]
        public string? NextPageToken { get; set; }

        [JsonProperty("items")]
        public List<YouTubePlaylist> Items { get; set; } = new();
    }

    // ──────────────────────────────────────────────────────────────
    // Playlist items response (GET /youtube/v3/playlistItems)
    // ──────────────────────────────────────────────────────────────
    public class YouTubeVideoSnippet
    {
        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;

        // Channel that uploaded the video — for auto-generated channels this is
        // "Artist Name - Topic"
        [JsonProperty("videoOwnerChannelTitle")]
        public string VideoOwnerChannelTitle { get; set; } = string.Empty;
    }

    public class YouTubePlaylistItem
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("snippet")]
        public YouTubeVideoSnippet Snippet { get; set; } = new();
    }

    public class YouTubePlaylistItemListResponse
    {
        [JsonProperty("nextPageToken")]
        public string? NextPageToken { get; set; }

        [JsonProperty("items")]
        public List<YouTubePlaylistItem> Items { get; set; } = new();
    }

    // ──────────────────────────────────────────────────────────────
    // Channel list response (GET /youtube/v3/channels?mine=true)
    // ──────────────────────────────────────────────────────────────
    public class YouTubeChannelSnippet
    {
        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;
    }

    public class YouTubeChannel
    {
        [JsonProperty("snippet")]
        public YouTubeChannelSnippet Snippet { get; set; } = new();
    }

    public class YouTubeChannelListResponse
    {
        [JsonProperty("items")]
        public List<YouTubeChannel> Items { get; set; } = new();
    }

    // ──────────────────────────────────────────────────────────────
    // Proxy interface + implementation
    // ──────────────────────────────────────────────────────────────
    public interface IYouTubeMusicProxy
    {
        YouTubeTokenResponse ExchangeCode(
            string clientId, string clientSecret,
            string redirectUri, string code,
            string tokenUrl);

        YouTubeTokenResponse RefreshAccessToken(
            string clientId, string clientSecret,
            string refreshToken, string tokenUrl);

        string GetChannelTitle(string accessToken);

        List<YouTubePlaylist> GetUserPlaylists(string accessToken);

        List<YouTubePlaylistItem> GetPlaylistItems(string accessToken, string playlistId);
    }

    public class YouTubeMusicProxy : IYouTubeMusicProxy
    {
        private const string ChannelsEndpoint = "https://www.googleapis.com/youtube/v3/channels";
        private const string PlaylistsEndpoint = "https://www.googleapis.com/youtube/v3/playlists";
        private const string PlaylistItemsEndpoint = "https://www.googleapis.com/youtube/v3/playlistItems";

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public YouTubeMusicProxy(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public YouTubeTokenResponse ExchangeCode(
            string clientId, string clientSecret,
            string redirectUri, string code,
            string tokenUrl)
        {
            var body = string.Join("&",
                $"grant_type=authorization_code",
                $"code={Uri.EscapeDataString(code)}",
                $"client_id={Uri.EscapeDataString(clientId)}",
                $"client_secret={Uri.EscapeDataString(clientSecret)}",
                $"redirect_uri={Uri.EscapeDataString(redirectUri)}");

            return PostFormForToken(tokenUrl, body);
        }

        public YouTubeTokenResponse RefreshAccessToken(
            string clientId, string clientSecret,
            string refreshToken, string tokenUrl)
        {
            var body = string.Join("&",
                $"grant_type=refresh_token",
                $"refresh_token={Uri.EscapeDataString(refreshToken)}",
                $"client_id={Uri.EscapeDataString(clientId)}",
                $"client_secret={Uri.EscapeDataString(clientSecret)}");

            return PostFormForToken(tokenUrl, body);
        }

        public string GetChannelTitle(string accessToken)
        {
            var request = new HttpRequestBuilder(ChannelsEndpoint)
                .AddQueryParam("part", "snippet")
                .AddQueryParam("mine", "true")
                .AddQueryParam("maxResults", "1")
                .Build();
            request.Headers.Add("Authorization", $"Bearer {accessToken}");

            var response = _httpClient.Get<YouTubeChannelListResponse>(request);
            var channel = response.Resource?.Items?.Count > 0 ? response.Resource.Items[0] : null;
            return channel?.Snippet?.Title ?? "YouTube Music";
        }

        public List<YouTubePlaylist> GetUserPlaylists(string accessToken)
        {
            var result = new List<YouTubePlaylist>();
            string? pageToken = null;

            do
            {
                var builder = new HttpRequestBuilder(PlaylistsEndpoint)
                    .AddQueryParam("part", "snippet,contentDetails")
                    .AddQueryParam("mine", "true")
                    .AddQueryParam("maxResults", "50");

                if (pageToken != null)
                {
                    builder.AddQueryParam("pageToken", pageToken);
                }

                var request = builder.Build();
                request.Headers.Add("Authorization", $"Bearer {accessToken}");

                var response = _httpClient.Get<YouTubePlaylistListResponse>(request);
                var page = response.Resource;

                if (page?.Items == null || page.Items.Count == 0)
                {
                    break;
                }

                result.AddRange(page.Items);
                pageToken = page.NextPageToken;
            }
            while (pageToken != null);

            return result;
        }

        public List<YouTubePlaylistItem> GetPlaylistItems(string accessToken, string playlistId)
        {
            var result = new List<YouTubePlaylistItem>();
            string? pageToken = null;

            do
            {
                var builder = new HttpRequestBuilder(PlaylistItemsEndpoint)
                    .AddQueryParam("part", "snippet")
                    .AddQueryParam("playlistId", playlistId)
                    .AddQueryParam("maxResults", "50");

                if (pageToken != null)
                {
                    builder.AddQueryParam("pageToken", pageToken);
                }

                var request = builder.Build();
                request.Headers.Add("Authorization", $"Bearer {accessToken}");

                var response = _httpClient.Get<YouTubePlaylistItemListResponse>(request);
                var page = response.Resource;

                if (page?.Items == null || page.Items.Count == 0)
                {
                    break;
                }

                result.AddRange(page.Items);
                pageToken = page.NextPageToken;
            }
            while (pageToken != null);

            return result;
        }

        // ── helpers ───────────────────────────────────────────────

        private YouTubeTokenResponse PostFormForToken(string url, string formBody)
        {
            var request = new HttpRequestBuilder(url).Build();
            request.Method = HttpMethod.Post;
            request.Headers.ContentType = "application/x-www-form-urlencoded";
            request.ContentData = Encoding.UTF8.GetBytes(formBody);

            _logger.Trace("Posting OAuth token request to {0}", url);
            var response = _httpClient.Post<YouTubeTokenResponse>(request);
            return response.Resource;
        }
    }
}
