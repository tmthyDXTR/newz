using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;

namespace Nius
{
    /// <summary>
    /// Tracks article reading history, including opened articles and displayed articles
    /// </summary>
    public class ArticleHistory
    {
        private static readonly string HistoryFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Nius", "article_history.json");
            
        // Dictionary of article URLs to their read status timestamp
        public Dictionary<string, DateTime> OpenedArticles { get; set; } = new Dictionary<string, DateTime>();
        
        // Dictionary of article URLs to the first time they were displayed in the UI
        public Dictionary<string, DateTime> DisplayedArticles { get; set; } = new Dictionary<string, DateTime>();
        
        // Check if article was opened (read)
        public bool IsOpened(string url)
        {
            return !string.IsNullOrEmpty(url) && OpenedArticles.ContainsKey(url);
        }
        
        // Mark an article as opened (read)
        public void MarkOpened(string url)
        {
            if (!string.IsNullOrEmpty(url))
            {
                OpenedArticles[url] = DateTime.Now;
                // Also mark it as displayed
                MarkAsDisplayed(url);
                
                // Save immediately to ensure persistence
                Save();
            }
        }
        
        // Mark an article as displayed in the UI
        public void MarkAsDisplayed(string url)
        {
            if (!string.IsNullOrEmpty(url) && !DisplayedArticles.ContainsKey(url))
            {
                DisplayedArticles[url] = DateTime.Now;
            }
        }
        
        // Check if article has been displayed before
        public bool HasBeenDisplayed(string url)
        {
            return !string.IsNullOrEmpty(url) && DisplayedArticles.ContainsKey(url);
        }
        
        // Load history from file
        public static ArticleHistory Load()
        {
            try
            {
                // Create directory if it doesn't exist
                Directory.CreateDirectory(Path.GetDirectoryName(HistoryFilePath));

                if (File.Exists(HistoryFilePath))
                {
                    string json = File.ReadAllText(HistoryFilePath);
                    var options = new JsonSerializerOptions { 
                        PropertyNameCaseInsensitive = true,
                        WriteIndented = true
                    };
                    
                    var history = JsonSerializer.Deserialize<ArticleHistory>(json, options);
                    if (history != null)
                    {
                        // Ensure dictionaries aren't null
                        if (history.OpenedArticles == null)
                            history.OpenedArticles = new Dictionary<string, DateTime>();
                        if (history.DisplayedArticles == null)
                            history.DisplayedArticles = new Dictionary<string, DateTime>();
                            
                        return history;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading article history: {ex.Message}");
            }

            return new ArticleHistory();
        }

        // Save history to file
        public void Save()
        {
            try
            {
                // Create directory if it doesn't exist
                Directory.CreateDirectory(Path.GetDirectoryName(HistoryFilePath));

                // Serialize history to JSON
                var options = new JsonSerializerOptions { 
                    PropertyNameCaseInsensitive = true,
                    WriteIndented = true 
                };
                string json = JsonSerializer.Serialize(this, options);
                
                // Write to file
                File.WriteAllText(HistoryFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving article history: {ex.Message}");
            }
        }
        
        // Prune old entries (older than specified number of days)
        public void PruneOldEntries(int maxAgeDays = 30)
        {
            var cutoff = DateTime.Now.AddDays(-maxAgeDays);
            
            // Remove old entries from both dictionaries
            var oldOpenedKeys = OpenedArticles.Where(kvp => kvp.Value < cutoff).Select(kvp => kvp.Key).ToList();
            foreach (var key in oldOpenedKeys)
            {
                OpenedArticles.Remove(key);
            }
            
            var oldDisplayedKeys = DisplayedArticles.Where(kvp => kvp.Value < cutoff).Select(kvp => kvp.Key).ToList();
            foreach (var key in oldDisplayedKeys)
            {
                DisplayedArticles.Remove(key);
            }
            
            // Save changes if we removed any entries
            if (oldOpenedKeys.Any() || oldDisplayedKeys.Any())
            {
                Save();
            }
        }
    }
}
