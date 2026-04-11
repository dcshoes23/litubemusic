using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using NzbDrone.Core.Annotations;

namespace NzbDrone.Plugin.Litubemusic.ImportLists.YouTubeMusic
{
    public class YouTubeMusicPlaylistSettingsValidator
        : YouTubeMusicSettingsBaseValidator<YouTubeMusicPlaylistSettings>
    {
        public YouTubeMusicPlaylistSettingsValidator()
            : base()
        {
            RuleFor(s => s.PlaylistIds)
                .Must(ids => ids != null && ids.Any())
                .WithMessage("Select at least one YouTube Music playlist.");
        }
    }

    public class YouTubeMusicPlaylistSettings
        : YouTubeMusicSettingsBase<YouTubeMusicPlaylistSettings>
    {
        public YouTubeMusicPlaylistSettings()
        {
            PlaylistIds = Array.Empty<string>();
        }

        [FieldDefinition(20,
            Label = "Playlists",
            Type = FieldType.Playlist,
            HelpText = "Choose which YouTube Music playlists to import. Authenticate first, then click the field to load your playlists.")]
        public IEnumerable<string> PlaylistIds { get; set; }

        protected override AbstractValidator<YouTubeMusicPlaylistSettings> CreateValidator() =>
            new YouTubeMusicPlaylistSettingsValidator();
    }
}
