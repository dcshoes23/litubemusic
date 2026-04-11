# Litubemusic

A [Lidarr](https://lidarr.audio) plugin that imports your YouTube Music playlists as import lists. Authenticate with your Google account and Lidarr will automatically monitor and download albums by the artists in your playlists.

> **Requires Lidarr nightly/develop branch.** Plugins are not supported on the stable release.

---

## Features

- **OAuth 2.0 login** — sign in with your Google account directly from Lidarr's UI
- **Playlist picker** — choose which YouTube Music playlists to import
- **Smart metadata parsing** — extracts artist and album names from video titles and channel names, including YouTube Music's auto-generated "Artist - Topic" channels
- **Auto token refresh** — access tokens are refreshed in the background without re-authentication

---

## Google Cloud Setup

The plugin uses your own Google Cloud credentials so your private playlists stay private. This is a one-time setup.

### 1. Create a Google Cloud project

1. Go to [console.cloud.google.com](https://console.cloud.google.com)
2. Click **Select a project → New Project** and give it a name (e.g. `Litubemusic`)

### 2. Enable the YouTube Data API v3

1. In your project, go to **APIs & Services → Library**
2. Search for **YouTube Data API v3** and click **Enable**

### 3. Create OAuth 2.0 credentials

1. Go to **APIs & Services → Credentials**
2. Click **Create Credentials → OAuth client ID**
3. Set Application type to **Web application**
4. Under **Authorized redirect URIs**, add:
   ```
   http://localhost:8686/oauth.html
   ```
   Replace `localhost:8686` with your Lidarr URL if it differs.
5. Click **Create** and note your **Client ID** and **Client Secret**

> The OAuth consent screen will ask you to configure it. Set the user type to **External** and add your own Google account as a test user. You do not need to publish the app.

---

## Installation

### Via Lidarr Plugin Manager (recommended)

1. In Lidarr, go to **System → Plugins**
2. Paste this repository URL and click **Install**:
   ```
   https://github.com/dcshoes23/litubemusic
   ```

### Manual installation

Download the latest release ZIP and extract the DLL and `.lidarr.plugin` file into:

```
~/.config/Lidarr/plugins/dcshoes23/Lidarr.Plugin.Litubemusic/
```

Restart Lidarr.

---

## Usage

1. In Lidarr, go to **Settings → Import Lists → Add List**
2. Select **YouTube Music Playlists**
3. Enter your **Client ID** and **Client Secret** from the Google Cloud Console
4. Set **Lidarr Base URL** to the URL you use to access Lidarr (e.g. `http://localhost:8686`)
5. Click **Authenticate with YouTube Music** and sign in with your Google account
6. Once authenticated, click the **Playlists** field to load your playlists and select which ones to import
7. Save — Lidarr will sync artists and albums from your selected playlists on the configured interval

---

## How artist/album names are resolved

YouTube Music doesn't expose structured artist/album metadata via the YouTube Data API, so the plugin extracts names using these heuristics (in order):

| Priority | Condition | Artist | Album |
|---|---|---|---|
| 1 | Channel ends with `" - Topic"` | Channel name minus " - Topic" | Video title |
| 2 | Video title contains `" - "` | Left of first ` - ` | Right of first ` - ` |
| 3 | Channel title available | Channel title | Video title |
| 4 | Fallback | Video title | Video title |

Lidarr then searches MusicBrainz with the extracted artist and album names.

---

## Building from source

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- A Lidarr installation (nightly build)

### Steps

```bash
# 1. Clone the repo
git clone https://github.com/dcshoes23/litubemusic
cd litubemusic

# 2. Copy Lidarr reference DLLs (default path: /opt/Lidarr)
./setup.sh /path/to/your/Lidarr

# 3. Build (Debug)
dotnet build -c Debug -p:LidarrPath=/path/to/your/Lidarr

# 4. Build and install directly into Lidarr's plugins folder
dotnet build -c Release \
  -p:LidarrPath=/path/to/your/Lidarr \
  -p:LidarrPluginsDir=~/.config/Lidarr/plugins
```

After installing, restart Lidarr.

---

## Troubleshooting

**"No authorization code received" after authenticating**

Lidarr's `oauth.html` page may not pass URL query parameters back to the server on your build. Check the Lidarr logs for details. As a workaround, ensure the redirect URI registered in Google Cloud Console exactly matches `{LidarrBaseUrl}/oauth.html`.

**403 Forbidden from YouTube API**

The YouTube Data API v3 is not enabled in your Google Cloud project. Go to **APIs & Services → Library**, search for it, and click **Enable**.

**Tokens expire and re-authentication is required frequently**

This should not happen — the plugin uses refresh tokens to obtain new access tokens automatically. If it does, check that `access_type=offline` and `prompt=consent` were included during the initial authorization (they are by default). Re-authenticate once to get a fresh refresh token.

---

## License

MIT
