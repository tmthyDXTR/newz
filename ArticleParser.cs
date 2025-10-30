using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HtmlAgilityPack;
using Newz.Settings;
using Newz.Parsers;

namespace Newz
{
    public static class ArticleParser
    {
        private static readonly ArticleSummaryService _summaryService = new ArticleSummaryService();
        private static readonly TazArticleParser _tazParser = new TazArticleParser(_summaryService);
        private static readonly TagesschauArticleParser _tagesschauParser = new TagesschauArticleParser(_summaryService);
        private static readonly HLTVArticleParser _hltvParser = new HLTVArticleParser(_summaryService);
        private static readonly SpiegelArticleParser _spiegelParser = new SpiegelArticleParser(_summaryService);
        
        // Track dark mode state for all parsers
        private static bool _isDarkMode = false;
        
        public static void SetDarkMode(bool isDarkMode)
        {
            _isDarkMode = isDarkMode;
            _tazParser.SetDarkMode(isDarkMode);
            _tagesschauParser.SetDarkMode(isDarkMode);
            _hltvParser.SetDarkMode(isDarkMode);
            _spiegelParser.SetDarkMode(isDarkMode);
        }

        public static async Task<FlowDocument> ParseTazArticle(string html, FeedItem feedData, AppSettings settings, double windowWidth)
        {
            // Delegate to the TazArticleParser implementation
            return await _tazParser.ParseArticle(html, feedData, settings, windowWidth);
        }

        public static async Task<FlowDocument> ParseTagesschauArticle(string html, FeedItem feedData, AppSettings settings, double windowWidth)
        {
            // Delegate to the TagesschauArticleParser implementation
            return await _tagesschauParser.ParseArticle(html, feedData, settings, windowWidth);
        }

        public static async Task<FlowDocument> ParseHLTVArticle(string html, FeedItem feedData, AppSettings settings, double windowWidth)
        {
            // Delegate to the HLTVArticleParser implementation
            return await _hltvParser.ParseArticle(html, feedData, settings, windowWidth);
        }

        public static async Task<FlowDocument> ParseSpiegelArticle(string html, FeedItem feedData, AppSettings settings, double windowWidth)
        {
            // Delegate to the HLTVArticleParser implementation
            return await _spiegelParser.ParseArticle(html, feedData, settings, windowWidth);
        }
    }
}