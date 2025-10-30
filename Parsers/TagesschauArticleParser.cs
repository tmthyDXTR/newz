using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HtmlAgilityPack;
using Newz.Settings;

namespace Newz.Parsers
{
    public class TagesschauArticleParser : IArticleParser
    {
        private readonly ArticleSummaryService _summaryService;
        private bool _isDarkMode = false;

        public TagesschauArticleParser(ArticleSummaryService summaryService)
        {
            _summaryService = summaryService;
        }
        
        public void SetDarkMode(bool isDarkMode)
        {
            _isDarkMode = isDarkMode;
        }

        public Task<FlowDocument> ParseArticle(string html, FeedItem feedData, AppSettings settings, double windowWidth)
        {
            var htmlDoc = new HtmlAgilityPack.HtmlDocument();
            htmlDoc.LoadHtml(html);

            // Headline
            var headlineNode = htmlDoc.DocumentNode.SelectSingleNode("//h1[@class='seitenkopf__headline']");
            string headlineText = headlineNode != null ? headlineNode.InnerText.Trim() : "No headline found";

            // Date
            var dateNode = htmlDoc.DocumentNode.SelectSingleNode("//p[@class='metatextline']");
            string dateText = dateNode != null ? dateNode.InnerText.Trim() : "No date found";

            // Combine h2 and text paragraphs in one query to preserve order
            var contentNodes = htmlDoc.DocumentNode.SelectNodes("//h2 | //p[contains(@class,'textabsatz')]");

            // Extract the main text for summarization
            string mainText = string.Empty;
            if (contentNodes != null)
            {
                foreach (var node in contentNodes)
                {
                    string text = node.InnerText.Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        mainText += text + "\n";
                    }
                }
            }

            FlowDocument doc = new FlowDocument
            {
                Background = Brushes.Transparent, // Make background transparent to show paper texture
                FontFamily = new System.Windows.Media.FontFamily(settings.ArticleFontFamily),
                FontSize = settings.ArticleFontSize,
                TextAlignment = TextAlignment.Justify, // Better text alignment
                PagePadding = new Thickness(20),
                ColumnWidth = 300,
                ColumnGap = 20
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

            // Headline paragraph
            Paragraph headlinePara = new Paragraph(new Run($"Headline: {headlineText}"))
            {
                TextAlignment = TextAlignment.Center,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(settings.ParagraphSideMargin, 0, settings.ParagraphSideMargin, settings.ArticleMargin)
            };

            // Date paragraph
            Paragraph datePara = new Paragraph(new Run($"Published: {dateText}"))
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
            doc.Blocks.Add(datePara);

            // Add a title paragraph for the article section.
            Paragraph articleTitlePara = new Paragraph(new Run("Article:"))
            {
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(settings.ParagraphSideMargin, 0, settings.ParagraphSideMargin, settings.ArticleMargin)
            };
            doc.Blocks.Add(articleTitlePara);

            // Add content nodes
            if (contentNodes != null)
            {
                foreach (var node in contentNodes)
                {
                    string text = node.InnerText.Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        FontWeight weight = node.Name == "h2" ? FontWeights.SemiBold : FontWeights.Normal;
                        Paragraph para = new Paragraph(new Run(text))
                        {
                            FontWeight = weight,
                            Margin = new Thickness(settings.ParagraphSideMargin, 0, settings.ParagraphSideMargin, settings.ArticleMargin)
                        };
                        doc.Blocks.Add(para);
                    }
                }
            }

            // Start loading the summary asynchronously, but don't wait for it
            _ = _summaryService.LoadSummaryAsync(mainText, summaryPara, loadingAnimation);

            return Task.FromResult(doc);
        }
    }
}