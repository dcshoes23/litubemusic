using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Plugin.Litubemusic.ImportLists.YouTubeMusic
{
    /// <summary>
    /// Imports tracks from selected YouTube Music playlists.
    ///
    /// Artist / album extraction strategy (in priority order)
    /// ───────────────────────────────────────────────────────
    ///  1. Auto-generated "Artist - Topic" channel
    ///     videoOwnerChannelTitle ends with " - Topic" → strip suffix = artist name.
    ///     The video title becomes the album/track hint.
    ///
    ///  2. "Artist - Song Title" video title format
    ///     Split on the first " - " → left = artist, right = album.
    ///
    ///  3. Channel title fallback
    ///     Use videoOwnerChannelTitle as artist, video title as album.
    ///
    ///  4. Last resort
    ///     Use the video title for both artist and album.
    ///
    /// Lidarr will then fuzzy-search MusicBrainz using the returned artist + album strings.
    /// </summary>
    public class YouTubeMusicPlaylist
        : YouTubeMusicImportListBase<YouTubeMusicPlaylistSettings>
    {
        public override string Name => "YouTube Music Playlists";

        public YouTubeMusicPlaylist(
            IYouTubeMusicProxy proxy,
            IImportListRepository importListRepository,
            IImportListStatusService importListStatusService,
            IConfigService configService,
            IParsingService parsingService,
            Logger logger)
            : base(proxy, importListRepository,
                   importListStatusService, configService, parsingService, logger)
        {
        }

        // ── IImportList.Fetch ─────────────────────────────────────

        public override IList<ImportListItemInfo> Fetch()
        {
            var result = new List<ImportListItemInfo>();

            if (Settings.AccessToken.IsNullOrWhiteSpace())
            {
                _logger.Warn("[Litubemusic] Fetch skipped — not authenticated.");
                return result;
            }

            var accessToken = GetAccessToken();

            foreach (var playlistId in Settings.PlaylistIds)
            {
                if (playlistId.IsNullOrWhiteSpace())
                {
                    continue;
                }

                try
                {
                    FetchPlaylist(playlistId, accessToken, result);
                }
                catch (HttpException ex) when (ex.Response?.StatusCode == HttpStatusCode.Unauthorized)
                {
                    // Token expired mid-fetch — refresh once and retry
                    _logger.Info("[Litubemusic] Token expired while fetching playlist {0}. Refreshing.", playlistId);
                    DoRefreshToken();
                    accessToken = Settings.AccessToken;

                    try
                    {
                        FetchPlaylist(playlistId, accessToken, result);
                    }
                    catch (Exception retryEx)
                    {
                        _logger.Warn(retryEx, "[Litubemusic] Retry failed for playlist {0}.", playlistId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "[Litubemusic] Error fetching playlist {0}.", playlistId);
                }
            }

            return CleanupListItems(result);
        }

        private void FetchPlaylist(string playlistId, string accessToken, List<ImportListItemInfo> result)
        {
            _logger.Debug("[Litubemusic] Fetching playlist {0}", playlistId);
            var items = _proxy.GetPlaylistItems(accessToken, playlistId);

            foreach (var item in items)
            {
                var info = ParsePlaylistItem(item);
                if (info != null)
                {
                    result.Add(info);
                }
            }

            _logger.Debug("[Litubemusic] Playlist {0} → {1} item(s) parsed.", playlistId, items.Count);
        }

        // ── RequestAction — getPlaylists ──────────────────────────

        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            if (action == "getPlaylists")
            {
                return HandleGetPlaylists();
            }

            return base.RequestAction(action, query);
        }

        private object HandleGetPlaylists()
        {
            if (Settings.AccessToken.IsNullOrWhiteSpace())
            {
                _logger.Debug("[Litubemusic] getPlaylists: not authenticated.");
                return new { options = (object?)null };
            }

            try
            {
                var accessToken = GetAccessToken();
                var channelTitle = _proxy.GetChannelTitle(accessToken);
                var playlists = _proxy.GetUserPlaylists(accessToken);

                var options = playlists
                    .OrderBy(p => p.Snippet?.Title, StringComparer.OrdinalIgnoreCase)
                    .Select(p => new
                    {
                        id = p.Id,
                        name = p.Snippet?.Title.IsNotNullOrWhiteSpace() == true
                            ? p.Snippet.Title
                            : p.Id
                    })
                    .ToList();

                _logger.Debug("[Litubemusic] getPlaylists → {0} playlist(s)", options.Count);

                // Lidarr's FieldType.Playlist widget expects:
                // { options: { user: "...", playlists: [{ id, name }, ...] } }
                return new
                {
                    options = new { user = channelTitle, playlists = options }
                };
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "[Litubemusic] Failed to retrieve playlists.");
                return new { options = new { user = "YouTube Music", playlists = Array.Empty<object>() } };
            }
        }

        // ── Metadata parsing ──────────────────────────────────────

        private static ImportListItemInfo? ParsePlaylistItem(YouTubePlaylistItem item)
        {
            if (item?.Snippet == null)
            {
                return null;
            }

            var title = item.Snippet.Title?.Trim();
            var channelTitle = item.Snippet.VideoOwnerChannelTitle?.Trim();

            if (title.IsNullOrWhiteSpace())
            {
                return null;
            }

            // YouTube marks unavailable videos with these sentinel titles
            if (title == "Deleted video" || title == "Private video" || title == "[Private video]")
            {
                return null;
            }

            string artistName;
            string albumName;

            // ── Strategy 1: YouTube Music auto-generated "Artist - Topic" channel ──
            // YouTube Music creates official artist channels with " - Topic" suffix.
            // The channel title is the most reliable artist source for these.
            if (channelTitle.IsNotNullOrWhiteSpace() &&
                channelTitle!.EndsWith(" - Topic", StringComparison.OrdinalIgnoreCase))
            {
                artistName = channelTitle[..^" - Topic".Length].Trim();
                albumName = title;
            }
            // ── Strategy 2: "Artist - Song Title" in the video title ──
            else if (title!.Contains(" - "))
            {
                var dashIndex = title.IndexOf(" - ", StringComparison.Ordinal);
                artistName = title[..dashIndex].Trim();
                albumName = title[(dashIndex + 3)..].Trim();

                if (albumName.IsNullOrWhiteSpace())
                {
                    albumName = title;
                }
            }
            // ── Strategy 3: Channel title as artist fallback ──
            else if (channelTitle.IsNotNullOrWhiteSpace())
            {
                artistName = channelTitle!;
                albumName = title;
            }
            // ── Strategy 4: Last resort — title as both artist and album ──
            else
            {
                artistName = title;
                albumName = title;
            }

            if (artistName.IsNullOrWhiteSpace())
            {
                return null;
            }

            return new ImportListItemInfo
            {
                Artist = artistName,
                Album = albumName
                // MusicBrainzArtistId / MusicBrainzAlbumId are left empty;
                // Lidarr will perform a fuzzy name search via the LidarrAPI.
            };
        }
    }
}
