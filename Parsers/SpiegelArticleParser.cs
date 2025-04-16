using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Linq;
using System.Collections.Generic;
using HtmlAgilityPack;
using Nius.Settings;

namespace Nius.Parsers
{
    public class SpiegelArticleParser : IArticleParser
    {
        private readonly ArticleSummaryService _summaryService;
        private bool _isDarkMode = false;

        public SpiegelArticleParser(ArticleSummaryService summaryService)
        {
            _summaryService = summaryService;
        }

        public void SetDarkMode(bool isDarkMode)
        {
            _isDarkMode = isDarkMode;
        }

        public async Task<FlowDocument> ParseArticle(string html, FeedItem feedData, AppSettings settings, double windowWidth)
        {
            // Create an empty FlowDocument to start with
            var document = new FlowDocument
            {
                PagePadding = new Thickness(20),
                ColumnWidth = 300,
                ColumnGap = 20,
                FontFamily = new FontFamily(settings.ArticleFontFamily),
                FontSize = settings.ArticleFontSize,
                Background = Brushes.Transparent,
                Foreground = Brushes.Black,
                TextAlignment = TextAlignment.Justify
            };

            // Calculate page width based on window size
            document.PageWidth = windowWidth - (2 * settings.ParagraphSideMargin) - 40;

            try
            {
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);

                // Extract title - usually in a span with font-extrabold class in the header
                var titleNode = htmlDoc.DocumentNode.SelectSingleNode("//span[contains(@class, 'font-extrabold')]");
                // Fix: Use a property that exists on FeedItem
                string title = titleNode != null ? titleNode.InnerText.Trim() : (feedData?.ToString() ?? "No title");

                // Extract subtitle/kicker - usually in a span with font-bold class
                var kickerNode = htmlDoc.DocumentNode.SelectSingleNode("//span[contains(@class, 'font-bold')]");
                string kicker = kickerNode != null ? kickerNode.InnerText.Trim() : string.Empty;

                // Extract summary text
                var summaryNode = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(@class, 'RichText--sans')]");
                string summary = summaryNode != null ? summaryNode.InnerText.Trim() : string.Empty;

                // Extract authors - they appear in links after "By"
                var authorLinks = htmlDoc.DocumentNode.SelectNodes("//a[contains(@href, '/impressum/autor')]");
                List<string> authors = new List<string>();
                if (authorLinks != null)
                {
                    foreach (var authorLink in authorLinks)
                    {
                        authors.Add(authorLink.InnerText.Trim());
                    }
                }
                string authorText = authors.Count > 0 ? string.Join(", ", authors) : "Unknown author";

                // Extract publication date
                var timeNode = htmlDoc.DocumentNode.SelectSingleNode("//time");
                string pubDate = timeNode != null ? timeNode.InnerText.Trim() : (feedData?.PubDate ?? DateTime.Now.ToString());

                // Extract article image URL
                // Extract article image URL - check multiple possible image locations
                var imgNode = htmlDoc.DocumentNode.SelectSingleNode("//picture/img");
                if (imgNode == null)
                {
                    // Try alternative image format with spgfx-aiImg class
                    imgNode = htmlDoc.DocumentNode.SelectSingleNode("//img[contains(@class, 'spgfx-aiImg')]");
                }

                string imageUrl = imgNode != null ? imgNode.GetAttributeValue("src", "") : "";

                // Check for data-src attribute if src is empty (some images use data-src for lazy loading)
                if (string.IsNullOrEmpty(imageUrl) && imgNode != null)
                {
                    imageUrl = imgNode.GetAttributeValue("data-src", "");
                }

                // If still no image URL, try to get it from the feed data
                if (string.IsNullOrEmpty(imageUrl) && feedData?.ImageUrl != null)
                {
                    imageUrl = feedData.ImageUrl;
                }
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    Image img = new Image();
                    try
                    {
                        BitmapImage bmp = new BitmapImage(new Uri(imageUrl, UriKind.Absolute));
                        img.Source = bmp;
                        img.Width = 150;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("Error loading image: " + ex.Message);
                    }

                    BlockUIContainer imageContainer = new BlockUIContainer(img)
                    {
                        Margin = new Thickness(0, 0, 0, settings.ArticleMargin) // Match TazArticleParser margin
                    };
                    document.Blocks.Add(imageContainer);
                }

                // Create summary paragraph with loading placeholder and start animation
                Paragraph summaryPara = _summaryService.CreateSummaryParagraph("Loading summary", settings, _isDarkMode);

                // Start the loading animation
                var loadingAnimation = _summaryService.StartLoadingAnimation(summaryPara);

                // Add kicker if available
                if (!string.IsNullOrEmpty(kicker))
                {
                    Paragraph kickerPara = new Paragraph(new Run(kicker))
                    {
                        TextAlignment = TextAlignment.Center,
                        FontWeight = FontWeights.Bold,
                        FontStyle = FontStyles.Italic,
                        Margin = new Thickness(settings.ParagraphSideMargin, 0, settings.ParagraphSideMargin, settings.ArticleMargin)
                    };
                    document.Blocks.Add(kickerPara);
                }

                // Add title
                Paragraph titlePara = new Paragraph(new Run(title))
                {
                    TextAlignment = TextAlignment.Center,
                    FontWeight = FontWeights.Bold,
                    FontSize = settings.ArticleFontSize * 1.5,
                    Margin = new Thickness(settings.ParagraphSideMargin, 0, settings.ParagraphSideMargin, settings.ArticleMargin)
                };
                document.Blocks.Add(titlePara);

                // Add summary placeholder
                document.Blocks.Add(summaryPara);

                // Add author info
                Paragraph authorPara = new Paragraph(new Run($"By {authorText}"))
                {
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(settings.ParagraphSideMargin, 0, settings.ParagraphSideMargin, settings.ArticleMargin)
                };
                document.Blocks.Add(authorPara);

                // Add publication date
                Paragraph pubDatePara = new Paragraph(new Run($"Published: {pubDate}"))
                {
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(settings.ParagraphSideMargin, 0, settings.ParagraphSideMargin, settings.ArticleMargin * 2)
                };
                document.Blocks.Add(pubDatePara);

                // Extract main article content - usually in div with RichText class
                // Replace the existing content nodes selection line (line 167) with this updated version:
                var contentNodes = htmlDoc.DocumentNode.SelectNodes("//section[contains(@class, 'RichText')]//p | //div[contains(@class, 'RichText')]//p");
                {
                    // Add a title paragraph for the article section
                    Paragraph articleTitlePara = new Paragraph(new Run("Article:"))
                    {
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(settings.ParagraphSideMargin, 0, settings.ParagraphSideMargin, settings.ArticleMargin)
                    };
                    document.Blocks.Add(articleTitlePara);

                    foreach (var node in contentNodes)
                    {
                        string paraText = node.InnerText.Trim();
                        if (!string.IsNullOrEmpty(paraText))
                        {
                            Paragraph para = new Paragraph(new Run(paraText))
                            {
                                TextAlignment = TextAlignment.Justify,
                                Margin = new Thickness(settings.ParagraphSideMargin, 0, settings.ParagraphSideMargin, settings.ArticleMargin)
                            };
                            document.Blocks.Add(para);
                        }
                    }
                }

                // Extract the article text for the summary
                string articleText = string.Empty;
                if (contentNodes != null)
                {
                    articleText = string.Join("\n", contentNodes.Select(n => n.InnerText.Trim()));
                }

                // Start loading the summary asynchronously, but don't wait for it
                if (!string.IsNullOrEmpty(articleText))
                {
                    _ = _summaryService.LoadSummaryAsync(articleText, summaryPara, loadingAnimation);
                }
                else if (!string.IsNullOrEmpty(summary))
                {
                    // If we couldn't extract article text but have a summary, use that
                    _ = _summaryService.LoadSummaryAsync(summary, summaryPara, loadingAnimation);
                }
                else
                {
                    // No content to summarize
                    summaryPara.Inlines.Clear();
                    summaryPara.Inlines.Add(new Run("No content available for summary."));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing Spiegel article: {ex.Message}");

                // Add error message to document
                Paragraph errorPara = new Paragraph(new Run($"Error parsing article: {ex.Message}"))
                {
                    Foreground = Brushes.Red,
                    Margin = new Thickness(settings.ParagraphSideMargin)
                };
                document.Blocks.Add(errorPara);
            }

            return document;
        }
    }
}