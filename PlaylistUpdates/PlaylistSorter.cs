using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

namespace YoutubePlaylistSorter
{
    public class PlaylistSorter
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Console.WriteLine("YouTube Playlist Sorter, by Tyler Porter");
            Console.WriteLine("==================================");

            try
            {
                new PlaylistSorter().Run().Wait();
            }
            catch (AggregateException ex)
            {
                foreach (var e in ex.InnerExceptions)
                {
                    Console.WriteLine("Error: " + e.Message);
                }
            }
            Console.Read();
        }

        /// <summary>
        /// Runs the playlist sorter, which will first authenticate the user who is
        /// wanting to sort their playlist, then call the method to do the sorting.
        /// </summary>
        private async Task Run()
        {
            UserCredential credential;
            using (var stream = new FileStream("client_secrets.json", FileMode.Open, FileAccess.Read))
            {
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.Load(stream).Secrets,
                    // This OAuth 2.0 access scope allows for full read/write access to the
                    // authenticated user's account.
                    new[] { YouTubeService.Scope.Youtube },
                    "user",
                    CancellationToken.None,
                    new FileDataStore(this.GetType().ToString())
                );
            }

            var youtubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = this.GetType().ToString()
            });

            SortPlaylist(youtubeService);
        }
            

        /// <summary>
        /// Sorts a playlist given by the user in alphabetical order.
        /// </summary>
        /// <param name="youtubeService">Service to connect to YouTube</param>
        private async static void SortPlaylist(YouTubeService youtubeService)
        {
            Playlist requestedPlaylist = GetUserRequestedPlaylistAsync(youtubeService).Result;
            if (requestedPlaylist == null)
            {
                Console.WriteLine("Playlist does not exist.");
                return;
            }

            // Get all videos from playlist and get their names
            var playlistItemsReq = youtubeService.PlaylistItems.List("snippet,contentDetails");
            playlistItemsReq.PlaylistId = requestedPlaylist.Id;
            playlistItemsReq.MaxResults = 1000L;
            var requestedPlaylistResponse = await playlistItemsReq.ExecuteAsync();

            // Making a dictionary of video names and their video ids
            Dictionary<string, string> requestedPlaylistVideos = new();
            foreach (var video in requestedPlaylistResponse.Items)
            {
                requestedPlaylistVideos.Add(video.Snippet.Title, video.Snippet.ResourceId.VideoId);
            }

            // Sort video names (automatically alphabetical order)
            List<string> requestedPlaylistVideoNames = requestedPlaylistVideos.Keys.ToList();
            requestedPlaylistVideoNames.Sort();

            // Reinsert into dictionary but in order
            Dictionary<string, string> sortedPlaylistVideos = new();
            foreach (string videoName in requestedPlaylistVideoNames)
            {
                sortedPlaylistVideos.Add(videoName, requestedPlaylistVideos[videoName]);
            }

            // Create a new playlist to hold the sorted videos
            var newPlaylist = new Playlist();
            newPlaylist.Snippet = new PlaylistSnippet();
            newPlaylist.Snippet.Title = $"{requestedPlaylist.Snippet.Title} [SORTED]";
            newPlaylist.Snippet.Description = $"The sorted version of {requestedPlaylist.Snippet.Title}.";
            newPlaylist.Status = new PlaylistStatus();
            newPlaylist.Status.PrivacyStatus = "public";
            newPlaylist = await youtubeService.Playlists.Insert(newPlaylist, "snippet,status").ExecuteAsync();

            // Add all videos to new playlist
            foreach (KeyValuePair<string, string> video in sortedPlaylistVideos)
            {
                var newPlaylistItem = new PlaylistItem();
                newPlaylistItem.Snippet = new PlaylistItemSnippet();
                newPlaylistItem.Snippet.PlaylistId = newPlaylist.Id;
                newPlaylistItem.Snippet.ResourceId = new ResourceId();
                newPlaylistItem.Snippet.ResourceId.Kind = "youtube#video";
                newPlaylistItem.Snippet.ResourceId.VideoId = video.Value;
                newPlaylistItem = await youtubeService.PlaylistItems.Insert(newPlaylistItem, "snippet").ExecuteAsync();

                Console.WriteLine("Playlist item id {0} was added to playlist id {1}.", newPlaylistItem.Id, newPlaylist.Id);
            }

            Console.WriteLine("Sorting completed.");
            Console.WriteLine("Press ENTER to continue...");
            Console.ReadKey();
        }

        // Helper methods

        /// <summary>
        /// Gets the requested playlist from user's list of playlists
        /// </summary>
        /// <param name="youtubeService"></param>
        /// <returns>Playlist user requested, or null if it does not exist</returns>
        private static async Task<Playlist> GetUserRequestedPlaylistAsync(YouTubeService youtubeService)
        {
            // Get all user's playlists
            List<string> userPlaylistNames = new List<string>();
            var playlistsListRequest = youtubeService.Playlists.List("snippet");
            playlistsListRequest.Mine = true;
            var playlistsListResponse = await playlistsListRequest.ExecuteAsync();

            // For each playlist, get the playlist name
            foreach (var playlistItem in playlistsListResponse.Items)
            {
                var playlistName = playlistItem.Snippet.Title;
                userPlaylistNames.Add(playlistName);
            }

            // Read playlist that user wants to sort, and check if that playlist exists
            Console.Write("Enter playlist to get: ");
            string userRequest = Console.ReadLine();
            if (!userPlaylistNames.Contains(userRequest))
            {
                return null;
            }

            // If playlist exists, let's get it
            Playlist requestedPlaylist = new Playlist();
            foreach (var playlist in playlistsListResponse.Items)
            {
                var playlistName = playlist.Snippet.Title;
                if (playlistName == userRequest)
                {
                    Console.WriteLine($"\nPlaylist found: {playlistName}");
                    Console.WriteLine("==================================");
                    requestedPlaylist = playlist;
                    break;
                }
            }

            return requestedPlaylist;
        }
    }
}

