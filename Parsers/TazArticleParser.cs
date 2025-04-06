using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks; // Added missing namespace
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HtmlAgilityPack;
using Nius.Settings;

namespace Nius.Parsers
{
    public class TazArticleParser : IArticleParser
    {
        private readonly ArticleSummaryService _summaryService;
        private bool _isDarkMode = false;

        public TazArticleParser(ArticleSummaryService summaryService)
        {
            _summaryService = summaryService;
        }

        public void SetDarkMode(bool isDarkMode)
        {
            _isDarkMode = isDarkMode;
        }

        // Fixed return type to match interface
        public Task<FlowDocument> ParseArticle(string html, FeedItem feedData, AppSettings settings, double windowWidth)
        {
            var htmlDoc = new HtmlAgilityPack.HtmlDocument();
            htmlDoc.LoadHtml(html);

            var headlineNode = htmlDoc.DocumentNode.SelectSingleNode("//h2");
            string headlineText = headlineNode != null ? headlineNode.InnerText.Trim() : "No headline found";

            var authorNode = htmlDoc.DocumentNode.SelectSingleNode("//*[contains(@class, 'author-name-wrapper')]");
            string authorText = authorNode != null ? authorNode.InnerText.Trim() : "No author found";

            var mainTextNode = htmlDoc.DocumentNode.SelectSingleNode("//*[contains(@class, 'main-article-corpus')]");
            string mainTextRaw = mainTextNode != null ? mainTextNode.InnerText.Trim() : "No main content found";
            string mainText = Regex.Replace(mainTextRaw, @"\s{2,}", "\n");

            // Build a newspaper-style FlowDocument using settings
            FlowDocument doc = new FlowDocument
            {
                PagePadding = new Thickness(20),
                ColumnWidth = 300,
                ColumnGap = 20,
                FontFamily = new System.Windows.Media.FontFamily(settings.ArticleFontFamily),
                FontSize = settings.ArticleFontSize,
                Background = Brushes.Transparent, // Make background transparent to show paper texture
                Foreground = System.Windows.Media.Brushes.Black,
                TextAlignment = TextAlignment.Justify // Better text alignment
            };

            // Calculate the page width based on window size
            doc.PageWidth = windowWidth - (2 * settings.ParagraphSideMargin) - 40;

            if (!string.IsNullOrEmpty(feedData.ImageUrl))
            {
                Image img = new Image();
                try
                {
                    BitmapImage bmp = new BitmapImage(new Uri(feedData.ImageUrl, UriKind.Absolute));
                    img.Source = bmp;
                    img.Width = 150;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error loading thumbnail: " + ex.Message);
                }
                BlockUIContainer imageContainer = new BlockUIContainer(img)
                {
                    Margin = new Thickness(0, 0, 0, settings.ArticleMargin)
                };
                doc.Blocks.Add(imageContainer);
            }

            Paragraph headlinePara = new Paragraph(new Run($"Headline: {headlineText}"))
            {
                TextAlignment = TextAlignment.Center,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(settings.ParagraphSideMargin, 0, settings.ParagraphSideMargin, settings.ArticleMargin)
            };

            Paragraph authorPara = new Paragraph(new Run($"Author: {authorText}"))
            {
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(settings.ParagraphSideMargin, 0, settings.ParagraphSideMargin, settings.ArticleMargin)
            };

            // Add publication date paragraph
            Paragraph pubDatePara = new Paragraph(new Run($"Published: {feedData.PubDate}"))
            {
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(settings.ParagraphSideMargin, 0, settings.ParagraphSideMargin, settings.ArticleMargin)
            };

            // Create summary paragraph with loading placeholder and start animation
            Paragraph summaryPara = _summaryService.CreateSummaryParagraph("Loading summary", settings, _isDarkMode);
            
            // Start the loading animation
            var loadingAnimation = _summaryService.StartLoadingAnimation(summaryPara);

            doc.Blocks.Add(headlinePara);
            doc.Blocks.Add(summaryPara); // Add the summary placeholder right after headline
            doc.Blocks.Add(authorPara);
            doc.Blocks.Add(pubDatePara); // Add the publication date

            // Add a title paragraph for the article section.
            Paragraph articleTitlePara = new Paragraph(new Run("Article:"))
            {
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(settings.ParagraphSideMargin, 0, settings.ParagraphSideMargin, settings.ArticleMargin)
            };
            doc.Blocks.Add(articleTitlePara);

            // Split main text into individual lines and add each as its own Paragraph.
            // Each line gets a bottom margin of settings.ArticleMargin.
            string[] lines = mainText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                Paragraph linePara = new Paragraph(new Run(line))
                {
                    TextAlignment = TextAlignment.Justify,
                    Margin = new Thickness(settings.ParagraphSideMargin, 0, settings.ParagraphSideMargin, settings.ArticleMargin)
                };
                doc.Blocks.Add(linePara);
            }

            // Start loading the summary asynchronously, but don't wait for it
            _ = _summaryService.LoadSummaryAsync(mainText, summaryPara, loadingAnimation);

            // Return the document immediately as a completed task
            return Task.FromResult(doc);
        }
    }
}