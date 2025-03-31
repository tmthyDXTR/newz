using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Nius
{
    public class FeedService
    {
        private readonly HttpClient httpClient;

        public FeedService()
        {
            // Create a shared HttpClient with optimized settings
            httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Nius Feed Reader");
            httpClient.Timeout = TimeSpan.FromSeconds(10); // Set a reasonable timeout
        }

        public async Task<List<XDocument>> FetchFeedsAsync(List<string> feedUrls)
        {
            var results = new List<XDocument>();
            var tasks = new List<Task<XDocument>>();

            // Create tasks for all feeds
            foreach (string url in feedUrls)
            {
                tasks.Add(FetchFeedAsync(url));
            }

            // Wait for all to complete
            await Task.WhenAll(tasks);

            // Collect results
            foreach (var task in tasks)
            {
                try
                {
                    var doc = await task;
                    if (doc != null)
                    {
                        results.Add(doc);
                    }
                }
                catch
                {
                    // Skip failed feeds
                }
            }

            return results;
        }

        private async Task<XDocument> FetchFeedAsync(string url)
        {
            try
            {
                var response = await httpClient.GetStringAsync(url);
                return XDocument.Parse(response);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load feed {url}: {ex.Message}");
                return null;
            }
        }
    }
}
