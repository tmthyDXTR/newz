using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Newz.Settings;

namespace Newz
{
    public class ArticleSummaryService
    {
        // Base API endpoint for the summarization service
        private const string ApiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent";
        private readonly string _apiKey;

        public ArticleSummaryService()
        {
            _apiKey = LoadApiKey();
        }

        // Load API key from secrets.json file
        private string LoadApiKey()
        {
            try
            {
                string secretsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "secrets.json");
                
                // If secrets.json doesn't exist in the executable directory, try the project directory
                if (!File.Exists(secretsPath))
                {
                    string projectDir = Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory);
                    while (projectDir != null && !Directory.GetFiles(projectDir, "*.csproj").Any())
                    {
                        projectDir = Path.GetDirectoryName(projectDir);
                    }
                    
                    if (projectDir != null)
                    {
                        secretsPath = Path.Combine(projectDir, "secrets.json");
                    }
                }
                
                if (File.Exists(secretsPath))
                {
                    string json = File.ReadAllText(secretsPath);
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        return doc.RootElement
                            .GetProperty("ApiKeys")
                            .GetProperty("GoogleGemini")
                            .GetString();
                    }
                }
                
                Debug.WriteLine("secrets.json file not found. Using environment variable or fallback.");
                
                // Try to get from environment variable
                string envApiKey = Environment.GetEnvironmentVariable("GOOGLE_GEMINI_API_KEY");
                if (!string.IsNullOrEmpty(envApiKey))
                {
                    return envApiKey;
                }
                
                // Fallback to template message in development environments
                Debug.WriteLine("API key not found. Please create a secrets.json file based on the template.");
                return "YOUR_API_KEY_REQUIRED";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading API key: {ex.Message}");
                return "API_KEY_LOAD_ERROR";
            }
        }

        // Helper method to create a consistently formatted summary paragraph
        public Paragraph CreateSummaryParagraph(string summaryText, AppSettings settings, bool isDarkMode = false)
        {
            // Use appropriate background color based on dark mode
            SolidColorBrush backgroundBrush = isDarkMode 
                ? new SolidColorBrush(Color.FromRgb(0, 0, 0)) // Black background in dark mode
                : new SolidColorBrush(Color.FromRgb(245, 245, 220)); // Beige background in light mode
                
            // Use appropriate text color based on dark mode
            Brush foregroundBrush = isDarkMode
                ? Application.Current.Resources["DarkModeText"] as SolidColorBrush ?? Brushes.Beige
                : Brushes.Black;
                
            // Create the paragraph with appropriate styling
            var paragraph = new Paragraph(new Run(summaryText))
            {
                FontWeight = FontWeights.SemiBold,
                Background = backgroundBrush,
                Padding = new Thickness(10),
                Margin = new Thickness(settings.ParagraphSideMargin, 0, settings.ParagraphSideMargin, settings.ArticleMargin * 1.5),
                BorderBrush = isDarkMode ? Brushes.DarkGray : Brushes.Gray,
                BorderThickness = new Thickness(1),
                Foreground = foregroundBrush
            };
            
            return paragraph;
        }

        // Helper method to start the loading animation
        public DispatcherTimer StartLoadingAnimation(Paragraph loadingPara)
        {
            int dotCount = 0;
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };

            timer.Tick += (s, e) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Update dots animation
                    dotCount = (dotCount + 1) % 4;
                    string dots = new string('.', dotCount);
                    
                    // Check if we still have the loading text before updating
                    var run = loadingPara.Inlines.FirstInline as Run;
                    if (run != null && run.Text.StartsWith("Loading summary"))
                    {
                        run.Text = $"Loading summary{dots}";
                    }
                    else
                    {
                        // If the text has been replaced with actual summary, stop the timer
                        timer.Stop();
                    }
                });
            };

            timer.Start();
            return timer;
        }

        // Helper method to asynchronously load and update the summary
        public async Task LoadSummaryAsync(string mainText, Paragraph summaryPara, DispatcherTimer animationTimer)
        {
            try
            {
                // Generate summary from main text
                string summary = await SummarizeArticle(mainText);
                
                // Update the paragraph on the UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Clear existing content and add the new summary
                    summaryPara.Inlines.Clear();
                    summaryPara.Inlines.Add(new Run(summary));
                    
                    // Stop the animation timer
                    animationTimer.Stop();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating summary: {ex.Message}");
                
                // Update with error message on UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    summaryPara.Inlines.Clear();
                    summaryPara.Inlines.Add(new Run("Error loading summary."));
                    
                    // Stop the animation timer
                    animationTimer.Stop();
                });
            }
        }

        public async Task<string> SummarizeArticle(string fullText, int maxSentences = 5)
        {
            using (var client = new HttpClient())
            {
                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[]
                            {
                                new { text = $"Fasse den Artikel professionell und unvoreingenommen und nicht wertend in maximal {maxSentences} Sätze oder weniger zusammen (Antworte in der Ursprungssprache des folgenden Artikels):\n\n{fullText}" }
                            }
                        }
                    }
                };

                var jsonRequest = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonRequest, System.Text.Encoding.UTF8, "application/json");
                DateTime startTime = DateTime.Now;

                try
                {
                    var response = await client.PostAsync($"{ApiBaseUrl}?key={_apiKey}", content);
                    response.EnsureSuccessStatusCode();
                    var elapsedTime = (DateTime.Now - startTime).TotalMilliseconds;

                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    var responseDoc = JsonDocument.Parse(jsonResponse);

                    // Extract the text from the response
                    if (responseDoc.RootElement.TryGetProperty("candidates", out var candidates) &&
                        candidates.GetArrayLength() > 0)
                    {
                        var firstCandidate = candidates[0];

                        // Extract model info if available
                        string modelName = "gemini-2.0-flash";
                        if (firstCandidate.TryGetProperty("modelInfo", out var modelInfo) &&
                            modelInfo.TryGetProperty("name", out var modelNameElement))
                        {
                            modelName = modelNameElement.GetString() ?? modelName;
                        }

                        if (firstCandidate.TryGetProperty("content", out var contentElement) &&
                            contentElement.TryGetProperty("parts", out var parts) &&
                            parts.GetArrayLength() > 0)
                        {
                            if (parts[0].TryGetProperty("text", out var textElement))
                            {
                                string summaryText = textElement.GetString() ?? "Summary unavailable.";

                                // Calculate word count and reduction metrics
                                int originalWordCount = fullText.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
                                int summaryWordCount = summaryText.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
                                double reductionPercent = 100 - ((double)summaryWordCount / originalWordCount * 100);

                                // Add metadata header with word count reduction
                                string metaInfo = $"AI Summary (Model: {modelName}, Time: {elapsedTime:F0}ms, " +
                                                 $"Words: {originalWordCount}→{summaryWordCount} ({reductionPercent:F1}% reduction))\n" +
                                                 $"───────────────────────────────────\n";

                                return metaInfo + summaryText;
                            }
                        }
                    }

                    Debug.WriteLine($"Invalid API response format: {jsonResponse}");
                    return "Summary unavailable.";
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error calling API: {ex.Message}");
                    return "Error generating summary.";
                }
            }
        }
    }
}