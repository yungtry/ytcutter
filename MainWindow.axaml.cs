using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System.IO;
using System.Net.Http;
using System;
using Avalonia.Media;
using FFMpegCore;

namespace ytcutter
{
    public partial class MainWindow : Window
    {

        public MainWindow()
        {
            InitializeComponent();
            TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
            Background = new SolidColorBrush(Color.Parse("#FFFFFF"));
            if (OperatingSystem.IsWindows())
            {
                GlobalFFOptions.Configure(new FFOptions { BinaryFolder = "./bin"});
                if (Environment.OSVersion.Version.Build >= 22000) {
                    TransparencyLevelHint = new[] { WindowTransparencyLevel.Mica };
                    Background = null;
                }
            }
            else
            {
                GlobalFFOptions.Configure(new FFOptions { BinaryFolder = "/usr/bin/"});
            }
            
            Closing += (s, e) =>
            {
                Youtube.KillFFmpegProcess();
            };
        }

        private static bool CheckFFmpegAvailability()
        {
            try
            {
                using var process = new Process();
                process.StartInfo.FileName = "ffmpeg";
                process.StartInfo.Arguments = "--version";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch
            {
                // If not in PATH, check the local bin folder
                string binPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin");
                string ffmpegExe = Path.Combine(binPath, "ffmpeg.exe");

                if (File.Exists(ffmpegExe))
                {
                    return true;
                }
                else 
                {
                    return false;
                }
                
            }
        }

        public bool IsValidTimeRange(double startTime, double finishTime)
        {
            return startTime >= 0 && finishTime > startTime && finishTime <= 86400; // 86400 seconds = 24 hours
        }

        private void disableControlls()
        {
            Dispatcher.UIThread.Post(() =>
            {
                cutButton.IsEnabled = false;
                youtubeUrl.IsEnabled = false;
                startInput.IsEnabled = false;
                finishInput.IsEnabled = false;
                progressRing.IsActive = true;
            });
        }

        public void enableControlls()
        {
            Dispatcher.UIThread.Post(() =>
            {
                cutButton.IsEnabled = true;
                youtubeUrl.IsEnabled = true;
                startInput.IsEnabled = true;
                finishInput.IsEnabled = true;
                progressRing.IsActive = false;
            });
        }

        public void updateStatus(string Status, int Progress)
        {
            Dispatcher.UIThread.Post(() =>
            {
                statusText.Text = "Status: " + Status;
                progressBar.Value = Progress;
            });
        }

        async private void cutButton_OnClick(object? sender, RoutedEventArgs e)
        {
            updateStatus("Button clicked", 5);
            string path = Directory.GetCurrentDirectory();
            Debug.WriteLine("The current directory is {0}", path);
            try
            {
                disableControlls();
                if (string.IsNullOrWhiteSpace(youtubeUrl.Text))
                {
                    updateStatus("Please enter a YouTube URL", 0);
                    return;
                }

                if (!Youtube.IsValidYoutubeUrl(youtubeUrl.Text))
                {
                    updateStatus("Please enter a valid YouTube URL", 0);
                    return;
                }

                if (!CheckFFmpegAvailability())
                {
                    updateStatus("FFmpeg is not available", 0);
                    return;
                }

                if (!double.TryParse(startInput.Text, out double startTime) || 
                    !double.TryParse(finishInput.Text, out double finishTime))
                {
                    updateStatus("Start and finish times must be valid numbers in seconds", 0);
                    return;
                }

                if (!IsValidTimeRange(startTime, finishTime))
                {
                    updateStatus("Invalid time range", 0);
                    return;
                }

                updateStatus("Retrieving thumbnail", 10); ;
                string title = await Youtube.GetVideoTitle(youtubeUrl.Text);
                videoTitle.Text = title;
                string thumbnailUrl = await Youtube.GetVideoThumbnail(youtubeUrl.Text);

                updateStatus("Downloading the thumbnail", 15);
                using (var httpClient = new HttpClient())
                {
                    var imageBytes = await httpClient.GetByteArrayAsync(thumbnailUrl);
                    using (var ms = new MemoryStream(imageBytes))
                    {
                        videoThumbnail.Source = new Bitmap(ms);
                    }
                }

                updateStatus($"Opening youtube connection for: {title}", 25);
                await Youtube.DownloadVideo(youtubeUrl.Text, Convert.ToDouble(startInput.Text), Convert.ToDouble(finishInput.Text), this);
                updateStatus($"Download completed! Check the '{title}.webm' file in output directory.", 100);
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"An error occurred: {ex.Message}");
                updateStatus($"An error occurred: {ex.Message}", 0);
                enableControlls();
            }
            finally
            {
                enableControlls();
            }
        }

        private void TimeInput_TextChanged(object? sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                string? input = textBox.Text;
                if (string.IsNullOrWhiteSpace(input)) return;

                // Check if input matches MM:SS format
                if (input.Contains(":"))
                {
                    string[] parts = input.Split(':');
                    if (parts.Length == 2)
                    {
                        // Don't convert if the seconds part is incomplete (less than 2 digits)
                        if (parts[1].Length < 2) return;

                        if (int.TryParse(parts[0], out int minutes) && int.TryParse(parts[1], out int seconds))
                        {
                            if (seconds >= 0 && seconds < 60 && minutes >= 0)
                            {
                                int totalSeconds = minutes * 60 + seconds;
                                // Only update if the value actually changed
                                if (textBox.Text != totalSeconds.ToString())
                                {
                                    textBox.Text = totalSeconds.ToString();
                                }
                            }
                        }
                    }
                }
            }
        }
        
    }
}