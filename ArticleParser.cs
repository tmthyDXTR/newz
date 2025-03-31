using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HtmlAgilityPack;
using Nius.Settings;

namespace Nius
{
    public static class ArticleParser
    {
        public static FlowDocument ParseTazArticle(string html, FeedItem feedData, AppSettings settings, double windowWidth)
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

            // Generate summary from main text
            string summary = SummarizeArticle(mainText);

            // Add summary paragraph after the headline
            Paragraph summaryPara = new Paragraph(new Run(summary))
            {
                FontWeight = FontWeights.SemiBold,
                FontStyle = FontStyles.Italic,
                Background = new SolidColorBrush(Color.FromRgb(245, 245, 220)),
                Padding = new Thickness(10),
                Margin = new Thickness(settings.ParagraphSideMargin, 0, settings.ParagraphSideMargin, settings.ArticleMargin * 1.5),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1)
            };

            doc.Blocks.Add(headlinePara);
            doc.Blocks.Add(summaryPara); // Add the summary right after headline
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

            return doc;
        }

        public static FlowDocument ParseTagesschauArticle(string html, FeedItem feedData, AppSettings settings, double windowWidth)
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

            FlowDocument doc = new FlowDocument
            {
                Background = Brushes.Transparent, // Make background transparent to show paper texture
                FontFamily = new System.Windows.Media.FontFamily(settings.ArticleFontFamily),
                FontSize = settings.ArticleFontSize,
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

            // Headline and date on top
            doc.Blocks.Add(new Paragraph(new Run($"Headline: {headlineText}")) { FontWeight = FontWeights.Bold });
            doc.Blocks.Add(new Paragraph(new Run($"Published: {dateText}")) { FontStyle = FontStyles.Italic });

            if (contentNodes != null)
            {
                foreach (var node in contentNodes)
                {
                    string text = node.InnerText.Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        FontWeight weight = node.Name == "h2" ? FontWeights.SemiBold : FontWeights.Normal;
                        doc.Blocks.Add(new Paragraph(new Run(text)) { FontWeight = weight });
                    }
                }
            }
            return doc;
        }

        private static string SummarizeArticle(string fullText, int maxSentences = 5)
        {
            // Basic summarization: extract first few sentences as summary
            var sentences = Regex.Split(fullText, @"(?<=[.!?])\s+")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            if (sentences.Count <= maxSentences)
                return fullText;

            return string.Join(" ", sentences.Take(maxSentences)) + "...";
        }
    }
}