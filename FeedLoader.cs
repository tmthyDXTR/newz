using System;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.IO;

namespace Nius
{
    public static class FeedLoader
    {
        private static readonly HttpClient client;
        private static readonly HttpClient enhancedClient; // Special client for sites with stricter anti-scraping
        private static readonly Dictionary<string, DateTime> lastAccessTimes = new Dictionary<string, DateTime>();
        private static readonly Random random = new Random();

        static FeedLoader()
        {
            // Initialize standard HttpClient with browser-like headers
            client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            client.Timeout = TimeSpan.FromSeconds(30);
            
            // Initialize enhanced HttpClient with more sophisticated browser simulation
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                UseCookies = true,
                CookieContainer = new CookieContainer()
            };
            
            enhancedClient = new HttpClient(handler);
            
            // Use the latest Chrome version with a realistic user agent
            enhancedClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            enhancedClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
            // enhancedClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            // enhancedClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
            
            // Add browser fingerprinting headers that sites check for bots
            enhancedClient.DefaultRequestHeaders.Add("Sec-Ch-Ua", "\"Chromium\";v=\"122\", \"Not(A:Brand\";v=\"24\", \"Google Chrome\";v=\"122\"");
            enhancedClient.DefaultRequestHeaders.Add("Sec-Ch-Ua-Mobile", "?0");
            enhancedClient.DefaultRequestHeaders.Add("Sec-Ch-Ua-Platform", "\"Windows\"");
            enhancedClient.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
            enhancedClient.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
            enhancedClient.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
            enhancedClient.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
            enhancedClient.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
            enhancedClient.DefaultRequestHeaders.Add("Priority", "u=0, i");
            
            // Longer timeout for challenging sites
            enhancedClient.Timeout = TimeSpan.FromSeconds(60);
        }

        public static async Task<List<XDocument>> FetchFeedsAsync(IEnumerable<string> feedUrls)
        {
            List<XDocument> documents = new List<XDocument>();
            
            foreach (var url in feedUrls)
            {
                Debug.WriteLine($"Trying to load feed: {url}");
                try
                {
                    string data = await client.GetStringAsync(url);
                    documents.Add(XDocument.Parse(data));
                    Debug.WriteLine($"Successfully loaded feed: {url}");
                }
                catch (Exception ex) 
                { 
                    Debug.WriteLine($"Error loading feed {url}: {ex.Message}");
                    // Handle errors gracefully
                }
            }
            
            return documents;
        }
        
        public static async Task<string> FetchArticleAsync(string url)
        {
            Debug.WriteLine($"Fetching article: {url}");
            
            // Determine if we should use the enhanced client based on domain
            bool useEnhancedClient = IsProtectedSite(url);
            var selectedClient = useEnhancedClient ? enhancedClient : client;
            
            // Implement rate limiting per domain
            string domain = ExtractDomain(url);
            
            // Respect rate limits for the domain - space out requests to the same domain
            if (lastAccessTimes.ContainsKey(domain))
            {
                TimeSpan timeSinceLastAccess = DateTime.Now - lastAccessTimes[domain];
                if (timeSinceLastAccess.TotalSeconds < 5) // At least 5 seconds between requests to same domain
                {
                    int delayMs = (5000 - (int)timeSinceLastAccess.TotalMilliseconds) + random.Next(1000, 3000);
                    Debug.WriteLine($"Rate limiting in effect for {domain}. Waiting {delayMs}ms before next request");
                    await Task.Delay(delayMs);
                }
            }
            
            // Update last access time
            lastAccessTimes[domain] = DateTime.Now;
            
            try
            {
                if (useEnhancedClient)
                {
                    Debug.WriteLine("Using enhanced client with anti-scraping countermeasures");
                    
                    // Add a random delay to appear more human-like (between 1-4 seconds)
                    await Task.Delay(random.Next(1000, 4000));
                    
                    // Create a new request with custom headers
                    using (var requestMessage = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        // Simulate coming from Google search results - one of the most common referrers
                        requestMessage.Headers.Referrer = new Uri("https://www.google.com/search?q=hltv+news");
                        
                        // Add a Cache-Control header like real browsers do
                        requestMessage.Headers.Add("Cache-Control", "max-age=0");
                        
                        // Add some randomness to headers to avoid fingerprinting
                        if (random.Next(0, 2) == 0)
                        {
                            requestMessage.Headers.Add("DNT", "1"); // Do Not Track - some browsers send this
                        }
                        
                        // Simulate a browser's "Accept" header preference order with slight variations
                        string[] acceptVariations = new[] {
                            "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8",
                            "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8",
                            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"
                        };
                        requestMessage.Headers.Add("Accept", acceptVariations[random.Next(0, acceptVariations.Length)]);
                        
                        // Send the request
                        var response = await selectedClient.SendAsync(requestMessage);
                        response.EnsureSuccessStatusCode();
                        return await response.Content.ReadAsStringAsync();
                    }
                }
                else
                {
                    // Use the standard client for other sites
                    // Still add a small delay to be a good citizen
                    await Task.Delay(random.Next(200, 800));
                    return await selectedClient.GetStringAsync(url);
                }
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"Error fetching article {url}: {ex.Message}");
                
                if (ex.StatusCode == HttpStatusCode.Forbidden || ex.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    Debug.WriteLine("Access denied (403) or rate limited (429). Site may have anti-scraping protection.");
                    
                    // Attempt to visit the HLTV homepage first, then try again (multi-step approach)
                    if (url.Contains("hltv.org") && !url.Equals("https://www.hltv.org/"))
                    {
                        Debug.WriteLine("Attempting multi-step approach for HLTV...");
                        try
                        {
                            // First visit the homepage to get cookies
                            using (var homeRequest = new HttpRequestMessage(HttpMethod.Get, "https://www.hltv.org/"))
                            {
                                homeRequest.Headers.Referrer = new Uri("https://www.google.com/");
                                var homeResponse = await enhancedClient.SendAsync(homeRequest);
                                
                                if (homeResponse.IsSuccessStatusCode)
                                {
                                    // Wait a bit like a human would
                                    await Task.Delay(random.Next(50, 250));
                                    
                                    // Now try the article again
                                    using (var retryRequest = new HttpRequestMessage(HttpMethod.Get, url))
                                    {
                                        retryRequest.Headers.Referrer = new Uri("https://www.hltv.org/");
                                        var retryResponse = await enhancedClient.SendAsync(retryRequest);
                                        
                                        if (retryResponse.IsSuccessStatusCode)
                                        {
                                            return await retryResponse.Content.ReadAsStringAsync();
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception retryEx)
                        {
                            Debug.WriteLine($"Multi-step approach failed: {retryEx.Message}");
                        }
                    }
                    
                    return $"<html><body><h1>Unable to access article</h1><p>The website has blocked our access. This may be due to anti-scraping measures.</p><p>Error details: {ex.Message}</p></body></html>";
                }
                
                return $"<html><body><h1>Error Loading Article</h1><p>{ex.Message}</p></body></html>";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unexpected error fetching article {url}: {ex.Message}");
                return $"<html><body><h1>Error Loading Article</h1><p>{ex.Message}</p></body></html>";
            }
        }
        
        // Helper method to determine if a site needs special handling
        private static bool IsProtectedSite(string url)
        {
            string domain = ExtractDomain(url);
            
            // List of sites known to have strict anti-scraping measures
            string[] protectedDomains = {
                "hltv.org", 
                "espn.com", 
                "nytimes.com",
                "bloomberg.com"
            };
            
            foreach (var protectedDomain in protectedDomains)
            {
                if (domain.Contains(protectedDomain))
                {
                    return true;
                }
            }
            
            return false;
        }
        
        // Extract domain from URL for rate limiting
        private static string ExtractDomain(string url)
        {
            try
            {
                Uri uri = new Uri(url);
                return uri.Host;
            }
            catch
            {
                // Fallback if URL parsing fails
                int startIndex = url.IndexOf("://");
                if (startIndex != -1)
                {
                    startIndex += 3;
                    int endIndex = url.IndexOf('/', startIndex);
                    if (endIndex != -1)
                    {
                        return url.Substring(startIndex, endIndex - startIndex);
                    }
                    return url.Substring(startIndex);
                }
                return url;
            }
        }
    }
}