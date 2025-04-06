using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace Nius.Settings
{
    public class AppSettings
    {
        private static readonly string SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Nius", "settings.json");

        // Settings properties with defaults
        public List<string> FeedUrls { get; set; } = new List<string>
        {
            "https://taz.de/!p4608;rss/",
            // "https://www.tagesschau.de/infoservices/alle-meldungen-100~rss2.xml",
            "https://www.tagesschau.de/inland/index~rss2.xml",
            "https://www.tagesschau.de/investigativ/index~rss2.xml",
            "https://www.tagesschau.de/wirtschaft/index~rss2.xml",
            "https://www.tagesschau.de/ausland/index~rss2.xml",
            "https://www.tagesschau.de/inland/regional/bayern/index~rss2.xml",
            "https://www.hltv.org/rss/news"
        };
        public string ArticleFontFamily { get; set; } = "Ubuntu Mono";
        public double ArticleFontSize { get; set; } = 16;
        public Color ArticleBackgroundColor { get; set; } = Colors.White;
        public double ArticleMargin { get; set; } = 10;
        public double ParagraphSideMargin { get; set; } = 20;
        public int MouseWheelScrollLines { get; set; } = 3;
        public bool IsDarkMode { get; set; } = false;

        // Load settings from file or create default settings
        public static AppSettings Load()
        {
            try
            {
                // Create directory if it doesn't exist
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath));

                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    return settings ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
            }

            return new AppSettings();
        }

        // Save settings to file
        public void Save()
        {
            try
            {
                // Create directory if it doesn't exist
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath));

                // Serialize settings to JSON
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(this, options);
                
                // Write to file
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
            }
        }
    }
}
