using YoutubeExplode;
using System.Threading.Tasks;
using YoutubeExplode.Videos.Streams;
using System.Linq;
using System.IO;
using System;
using System.Diagnostics;
using FFMpegCore;

namespace ytcutter
{
    class Youtube
    {
        private static YoutubeClient youtube = new YoutubeClient();

        public static void KillFFmpegProcess()
        {
            try
            {
                var processes = Process.GetProcessesByName("ffmpeg");
                foreach (var process in processes)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(true);
                        }
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception)
            {
                // Ignore any errors during cleanup
            }
        }

        public static async Task<string> GetVideoTitle(string url)
        {
            var video = await youtube.Videos.GetAsync(url);
            return video.Title;
        }

        public static async Task<string> GetVideoThumbnail(string url){
            var video = await youtube.Videos.GetAsync(url);
            return video.Thumbnails.First().Url;
        }

        public static bool IsValidYoutubeUrl(string url)
        {
            try
            {
                var videoId = YoutubeExplode.Videos.VideoId.TryParse(url);
                return videoId.HasValue;
            }
            catch
            {
                return false;
            }
        }

        public static async Task DownloadVideo(string url, double startTime, double finishTime, MainWindow mainWindow)
        {
            if (!IsValidYoutubeUrl(url))
            {
                throw new ArgumentException("Invalid YouTube URL. Please provide a valid YouTube video URL.");
            }

            if (!mainWindow.IsValidTimeRange(startTime, finishTime))
            {
                throw new ArgumentException("Invalid time range. Start time must be non-negative, finish time must be greater than start time, and both must be within 24 hours.");
            }

            try
            {
                mainWindow.updateStatus("Updating video manifests", 30);
                var streamManifest = await youtube.Videos.Streams.GetManifestAsync(url);
                var video = await youtube.Videos.GetAsync(url);

                // Clean up the video title for use in filename
                var safeTitle = string.Join("", video.Title.Split(Path.GetInvalidFileNameChars()));
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var baseFileName = $"{timestamp}_{safeTitle}";

                var audioStreamInfo = streamManifest
                    .GetAudioStreams()
                    .OrderByDescending(s => s.Bitrate)
                    .First();

                var videoStreamInfo = streamManifest
                    .GetVideoStreams()
                    .Where(s => s.Container == Container.Mp4)
                    .OrderByDescending(s => s.VideoQuality.MaxHeight)
                    .ThenByDescending(s => s.Bitrate)
                    .First();
                mainWindow.updateStatus($"Manifests updated (Selected quality: {videoStreamInfo.VideoQuality.Label})", 40);

                // Create output directory if it doesn't exist
                var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");
                Directory.CreateDirectory(outputDir);

                // Download audio and video separately
                var audioTempPath = Path.Combine(outputDir, $"{baseFileName}_audio_temp.mp4");
                var videoTempPath = Path.Combine(outputDir, $"{baseFileName}_video_temp.mp4");

                // Download audio (20% of progress)
                var audioProgress = new Progress<double>(p => 
                {
                    mainWindow.updateStatus($"Downloading audio: {(int)(p * 100)}%", 20 + (int)(p * 20));
                });
                await youtube.Videos.Streams.DownloadAsync(audioStreamInfo, audioTempPath, audioProgress);

                // Download video (20% of progress)
                var videoProgress = new Progress<double>(p => 
                {
                    mainWindow.updateStatus($"Downloading video: {(int)(p * 100)}%", 40 + (int)(p * 20));
                });
                await youtube.Videos.Streams.DownloadAsync(videoStreamInfo, videoTempPath, videoProgress);

                // Now use FFmpeg to merge, cut and convert
                var outputPath = Path.Combine(outputDir, $"{baseFileName}.webm");
                mainWindow.updateStatus("Starting video conversion...", 60);

                var lastProgress = 60;
                var duration = finishTime - startTime;
                await FFMpegArguments
                    .FromFileInput(videoTempPath)
                    .AddFileInput(audioTempPath)
                    .OutputToFile(outputPath, true, options => options
                        .WithVideoCodec("libvpx")
                        .WithAudioCodec("libvorbis")
                        .WithCustomArgument($"-ss {startTime} -t {duration}")
                        .WithCustomArgument("-b:v 2M -minrate 1M -maxrate 3M -crf 23")
                        .WithCustomArgument("-quality good -cpu-used 0")
                        .WithCustomArgument("-auto-alt-ref 1 -lag-in-frames 25")
                        .WithFastStart())
                    .NotifyOnProgress(currentDuration =>
                    {
                        var progressPercent = Math.Min(1.0, currentDuration.TotalSeconds / duration);
                        var newProgress = 60 + (int)(progressPercent * 35);
                        if (newProgress > lastProgress)
                        {
                            lastProgress = newProgress;
                            mainWindow.updateStatus($"Converting video: {(int)(progressPercent * 100)}%", newProgress);
                        }
                    })
                    .ProcessAsynchronously();

                // Clean up temporary files
                if (File.Exists(audioTempPath))
                {
                    File.Delete(audioTempPath);
                }
                if (File.Exists(videoTempPath))
                {
                    File.Delete(videoTempPath);
                }

                mainWindow.updateStatus("Download complete", 99);
            }
            catch (Exception ex)
            {
                mainWindow.updateStatus($"Failed to download video: {ex.Message}", 0);
                mainWindow.enableControlls();
                throw new Exception($"Failed to download video: {ex.Message}");
            }
        }
    }
}