using System;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Nius
{
    public static class FeedLoader
    {
        public static async Task<List<XDocument>> FetchFeedsAsync(IEnumerable<string> feedUrls)
        {
            List<XDocument> documents = new List<XDocument>();
            using (var client = new HttpClient())
            {
                foreach (var url in feedUrls)
                {
                    try
                    {
                        string data = await client.GetStringAsync(url);
                        documents.Add(XDocument.Parse(data));
                    }
                    catch { /* Handle errors gracefully */ }
                }
            }
            return documents;
        }
    }
}