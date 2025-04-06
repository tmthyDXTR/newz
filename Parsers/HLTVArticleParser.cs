using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HtmlAgilityPack;
using Nius.Settings;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nius.Parsers
{
    public class HLTVArticleParser : IArticleParser
    {
        private readonly ArticleSummaryService _summaryService;
        private bool _isDarkMode = false;

        public HLTVArticleParser(ArticleSummaryService summaryService)
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

            // Extract headline - directly target the headline class
            string headlineText = "No headline found";
            var headlineNode = htmlDoc.DocumentNode.SelectSingleNode("//h1[@class='headline']");
            if (headlineNode != null)
            {
                headlineText = headlineNode.InnerText.Trim();
            }

            // Extract author information
            string authorText = "No author found";
            var authorNodes = htmlDoc.DocumentNode.SelectNodes("//div[@class='article-info']//span[@class='author']");
            if (authorNodes != null && authorNodes.Count > 0)
            {
                authorText = string.Join(" & ", authorNodes.Select(node => node.InnerText.Trim()));
            }

            // Extract publication date
            string pubDate = feedData.PubDate;
            var dateNode = htmlDoc.DocumentNode.SelectSingleNode("//div[@class='date']");
            if (dateNode != null)
            {
                pubDate = dateNode.InnerText.Trim();
            }

            // Extract article description
            string descriptionText = "";
            var descriptionNode = htmlDoc.DocumentNode.SelectSingleNode("//p[@class='headertext']");
            if (descriptionNode != null)
            {
                descriptionText = descriptionNode.InnerText.Trim();
            }

            // Extract main content
            List<string> paragraphs = new List<string>();
            var mainContentNode = htmlDoc.DocumentNode.SelectSingleNode("//div[@class='newstext-con']");
            if (mainContentNode != null)
            {
                // Get all paragraph elements with class "news-block"
                var paragraphNodes = mainContentNode.SelectNodes(".//p[@class='news-block']");
                if (paragraphNodes != null)
                {
                    foreach (var para in paragraphNodes)
                    {
                        // Clean up text and add to paragraphs list
                        string paraText = Regex.Replace(para.InnerText.Trim(), @"\s{2,}", " ");
                        if (!string.IsNullOrWhiteSpace(paraText))
                        {
                            paragraphs.Add(paraText);
                        }
                    }
                }
            }

            // Get main article image
            string imageUrl = feedData.ImageUrl;
            string imageCaption = "";
            var imageNode = htmlDoc.DocumentNode.SelectSingleNode("//div[@class='image-con']/picture/img");
            var captionNode = htmlDoc.DocumentNode.SelectSingleNode("//div[@class='imagetext']");

            if (imageNode != null)
            {
                // Try to get the high-quality image src
                string imgSrc = imageNode.GetAttributeValue("src", "");
                if (!string.IsNullOrEmpty(imgSrc))
                {
                    imageUrl = imgSrc;
                }
            }

            if (captionNode != null)
            {
                imageCaption = captionNode.InnerText.Trim();
            }

            // Check for match results
            List<MatchResult> matchResults = new List<MatchResult>();
            var matchResultNode = htmlDoc.DocumentNode.SelectSingleNode("//div[@class='newsitem-match-result']");
            if (matchResultNode != null)
            {
                var matchResult = ParseMatchResult(matchResultNode);
                if (matchResult != null)
                {
                    matchResults.Add(matchResult);
                }
            }

            // Check for player stats tables
            List<TeamStats> teamStats = new List<TeamStats>();
            var statsTablesNodes = htmlDoc.DocumentNode.SelectNodes("//div[@class='newsitem-match-stats']");
            if (statsTablesNodes != null)
            {
                foreach (var tableNode in statsTablesNodes)
                {
                    var stats = ParseTeamStats(tableNode);
                    if (stats != null)
                    {
                        teamStats.Add(stats);
                    }
                }
            }

            // Build the FlowDocument with a newspaper-style layout
            FlowDocument doc = new FlowDocument
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

            // Calculate the page width based on window size
            doc.PageWidth = windowWidth - (2 * settings.ParagraphSideMargin) - 40;

            // Add headline
            Paragraph headlinePara = new Paragraph(new Run(headlineText))
            {
                TextAlignment = TextAlignment.Center,
                FontWeight = FontWeights.Bold,
                FontSize = settings.ArticleFontSize * 1.4,
                Margin = new Thickness(settings.ParagraphSideMargin, 0, settings.ParagraphSideMargin, settings.ArticleMargin)
            };
            doc.Blocks.Add(headlinePara);

            // Create summary paragraph with loading placeholder and start animation
            Paragraph summaryPara = _summaryService.CreateSummaryParagraph("Loading summary", settings, _isDarkMode);
            var loadingAnimation = _summaryService.StartLoadingAnimation(summaryPara);
            doc.Blocks.Add(summaryPara);

            // Add description text if available
            if (!string.IsNullOrEmpty(descriptionText))
            {
                Paragraph descriptionPara = new Paragraph(new Run(descriptionText))
                {
                    FontWeight = FontWeights.SemiBold,
                    FontStyle = FontStyles.Italic,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(settings.ParagraphSideMargin, 0, settings.ParagraphSideMargin, settings.ArticleMargin * 1.5)
                };
                doc.Blocks.Add(descriptionPara);
            }

            // Add image if available
            if (!string.IsNullOrEmpty(imageUrl))
            {
                Image img = new Image();
                try
                {
                    BitmapImage bmp = new BitmapImage(new Uri(imageUrl, UriKind.Absolute));
                    img.Source = bmp;
                    img.MaxWidth = doc.PageWidth - (2 * settings.ParagraphSideMargin);
                    img.Stretch = Stretch.Uniform;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error loading image: " + ex.Message);
                }

                BlockUIContainer imageContainer = new BlockUIContainer(img)
                {
                    Margin = new Thickness(settings.ParagraphSideMargin, 0, settings.ParagraphSideMargin, 5)
                };
                doc.Blocks.Add(imageContainer);

                // Add image caption if available
                if (!string.IsNullOrEmpty(imageCaption))
                {
                    Paragraph captionPara = new Paragraph(new Run(imageCaption))
                    {
                        FontStyle = FontStyles.Italic,
                        FontSize = settings.ArticleFontSize * 0.9,
                        TextAlignment = TextAlignment.Center,
                        Margin = new Thickness(settings.ParagraphSideMargin, 0, settings.ParagraphSideMargin, settings.ArticleMargin * 1.5)
                    };
                    doc.Blocks.Add(captionPara);
                }
            }

            // Add author and publication date
            Paragraph authorPara = new Paragraph()
            {
                TextAlignment = TextAlignment.Left,
                Margin = new Thickness(settings.ParagraphSideMargin, 0, settings.ParagraphSideMargin, 5)
            };
            authorPara.Inlines.Add(new Bold(new Run("By: ")));
            authorPara.Inlines.Add(new Run(authorText));
            doc.Blocks.Add(authorPara);

            Paragraph datePara = new Paragraph(new Run($"Published: {pubDate}"))
            {
                TextAlignment = TextAlignment.Left,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(settings.ParagraphSideMargin, 0, settings.ParagraphSideMargin, settings.ArticleMargin * 1.5)
            };
            doc.Blocks.Add(datePara);

            // Add separator before main content
            Paragraph separatorPara = new Paragraph()
            {
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(settings.ParagraphSideMargin, 0, settings.ParagraphSideMargin, settings.ArticleMargin)
            };
            separatorPara.Inlines.Add(new Run("* * *"));
            doc.Blocks.Add(separatorPara);

            // Add main content paragraphs
            foreach (string para in paragraphs)
            {
                Paragraph contentPara = new Paragraph(new Run(para))
                {
                    TextAlignment = TextAlignment.Justify,
                    Margin = new Thickness(settings.ParagraphSideMargin, 0, settings.ParagraphSideMargin, settings.ArticleMargin)
                };
                doc.Blocks.Add(contentPara);
            }

            // If no paragraphs were found, show an error message
            if (paragraphs.Count == 0)
            {
                Paragraph errorPara = new Paragraph(new Run("Unable to extract article content. Please view the original article on HLTV.org."))
                {
                    TextAlignment = TextAlignment.Center,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(settings.ParagraphSideMargin, 20, settings.ParagraphSideMargin, settings.ArticleMargin)
                };
                doc.Blocks.Add(errorPara);
            }

            // Display match results if available
            if (matchResults.Count > 0)
            {
                Paragraph matchTitlePara = new Paragraph(new Run("Match Results"))
                {
                    TextAlignment = TextAlignment.Center,
                    FontWeight = FontWeights.Bold,
                    FontSize = settings.ArticleFontSize * 1.2,
                    Margin = new Thickness(settings.ParagraphSideMargin, 20, settings.ParagraphSideMargin, settings.ArticleMargin)
                };
                doc.Blocks.Add(matchTitlePara);

                // Add each match result
                foreach (var match in matchResults)
                {
                    AddMatchResultToDocument(doc, match, settings);
                }
            }

            // Display team stats if available
            if (teamStats.Count > 0)
            {
                Paragraph statsTitlePara = new Paragraph(new Run("Player Statistics"))
                {
                    TextAlignment = TextAlignment.Center,
                    FontWeight = FontWeights.Bold,
                    FontSize = settings.ArticleFontSize * 1.2,
                    Margin = new Thickness(settings.ParagraphSideMargin, 20, settings.ParagraphSideMargin, settings.ArticleMargin)
                };
                doc.Blocks.Add(statsTitlePara);

                // Add each team's stats
                foreach (var stats in teamStats)
                {
                    AddTeamStatsToDocument(doc, stats, settings);
                }
            }

            // Start loading the summary asynchronously, but don't wait for it
            _ = _summaryService.LoadSummaryAsync(string.Join("\n", paragraphs), summaryPara, loadingAnimation);

            return Task.FromResult(doc);
        }

        private MatchResult ParseMatchResult(HtmlNode matchResultNode)
        {
            try
            {
                var result = new MatchResult();

                // Extract event name
                var eventNode = matchResultNode.SelectSingleNode(".//div[@class='newsitem-match-result-top']/span[@class='text-ellipsis bold']/a");
                if (eventNode != null)
                {
                    result.EventName = eventNode.InnerText.Trim();
                }

                // Extract match type
                var matchTypeNode = matchResultNode.SelectSingleNode(".//span[@class='newsitem-match-type bold']");
                if (matchTypeNode != null)
                {
                    result.MatchType = matchTypeNode.InnerText.Trim();
                }

                // Extract team 1 name
                var team1Node = matchResultNode.SelectSingleNode(".//div[@class='newsitem-match-result-team-con'][1]//div[@class='newsitem-match-result-team']/a");
                if (team1Node != null)
                {
                    result.Team1Name = team1Node.InnerText.Trim();
                }

                // Extract team 2 name
                var team2Node = matchResultNode.SelectSingleNode(".//div[@class='newsitem-match-result-team-con'][2]//div[@class='newsitem-match-result-team']/a");
                if (team2Node != null)
                {
                    result.Team2Name = team2Node.InnerText.Trim();
                }

                // Extract team logos
                var team1LogoNode = matchResultNode.SelectSingleNode(".//div[@class='newsitem-match-result-team-con'][1]//div[@class='newsitem-match-result-team-logo-con']/img");
                var team2LogoNode = matchResultNode.SelectSingleNode(".//div[@class='newsitem-match-result-team-con'][2]//div[@class='newsitem-match-result-team-logo-con']/img");

                if (team1LogoNode != null)
                {
                    result.Team1Logo = team1LogoNode.GetAttributeValue("src", "");
                }

                if (team2LogoNode != null)
                {
                    result.Team2Logo = team2LogoNode.GetAttributeValue("src", "");
                }

                // Extract scores
                var scoreNodes = matchResultNode.SelectNodes(".//div[@class='newsitem-match-result-score-con']//div[@class='newsitem-match-result-score']");
                if (scoreNodes != null && scoreNodes.Count >= 2)
                {
                    result.Team1Score = scoreNodes[0].InnerText.Trim();
                    result.Team2Score = scoreNodes[2].InnerText.Trim();
                }

                // Extract match date
                var dateNode = matchResultNode.SelectSingleNode(".//div[@class='newsitem-match-result-date']");
                if (dateNode != null)
                {
                    result.MatchDate = dateNode.InnerText.Trim();
                }

                // Extract map results
                var mapResultNodes = matchResultNode.SelectNodes(".//div[@class='newsitem-match-result-map']");
                if (mapResultNodes != null)
                {
                    foreach (var mapNode in mapResultNodes)
                    {
                        var mapResult = new MapResult();

                        var mapNameNode = mapNode.SelectSingleNode(".//a[@class='newsitem-match-result-map-name']");
                        if (mapNameNode != null)
                        {
                            mapResult.MapName = mapNameNode.InnerText.Trim();
                        }

                        var mapScoreNodes = mapNode.SelectNodes(".//div[contains(@class, 'newsitem-match-result-map-score')]");
                        if (mapScoreNodes != null && mapScoreNodes.Count >= 2)
                        {
                            mapResult.Team1Score = mapScoreNodes[0].InnerText.Trim();
                            mapResult.Team2Score = mapScoreNodes[1].InnerText.Trim();
                        }

                        result.Maps.Add(mapResult);
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing match result: {ex.Message}");
                return null;
            }
        }

        private TeamStats ParseTeamStats(HtmlNode statsNode)
        {
            try
            {
                var teamStats = new TeamStats();

                // Extract team name
                var teamHeaderNode = statsNode.SelectSingleNode(".//tr[@class='newsitem-match-stats-header']/th[@class='newsitem-match-stats-team']/a");
                if (teamHeaderNode != null)
                {
                    teamStats.TeamName = teamHeaderNode.InnerText.Trim();
                }

                // Extract team logo
                var teamLogoNode = statsNode.SelectSingleNode(".//tr[@class='newsitem-match-stats-header']/th[@class='newsitem-match-stats-team']/img");
                if (teamLogoNode != null)
                {
                    teamStats.TeamLogo = teamLogoNode.GetAttributeValue("src", "");
                }

                // Extract player stats
                var playerRowNodes = statsNode.SelectNodes(".//tr[@class='newsitem-match-stats-row']");
                if (playerRowNodes != null)
                {
                    foreach (var rowNode in playerRowNodes)
                    {
                        var playerStat = new PlayerStat();

                        // Player name
                        var playerNameNode = rowNode.SelectSingleNode(".//div[@class='newsitem-match-stats-player']/a");
                        if (playerNameNode != null)
                        {
                            playerStat.PlayerName = playerNameNode.InnerText.Trim();
                        }

                        // Player country
                        var playerCountryNode = rowNode.SelectSingleNode(".//div[@class='newsitem-match-stats-player']/img");
                        if (playerCountryNode != null)
                        {
                            playerStat.Country = playerCountryNode.GetAttributeValue("title", "");
                        }

                        // Player KD
                        var kdNode = rowNode.SelectSingleNode(".//td[@class='newsitem-match-stats-kd']");
                        if (kdNode != null)
                        {
                            playerStat.KD = kdNode.InnerText.Trim();
                        }
                        // Player +/-
                        var plusMinusNodeWon = rowNode.SelectSingleNode(".//td[@class='newsitem-match-stats-kdDiff won']");
                        var plusMinusNodeLost = rowNode.SelectSingleNode(".//td[@class='newsitem-match-stats-kdDiff lost']");
                        var plusMinusNodeNeutral = rowNode.SelectSingleNode(".//td[@class='newsitem-match-stats-kdDiff']");

                        if (plusMinusNodeWon != null)
                        {
                            // For positive values, ensure we have the + sign
                            string diffText = plusMinusNodeWon.InnerText.Trim();
                            playerStat.PlusMinus = diffText.StartsWith("+") ? diffText : $"+{diffText}";
                        }
                        else if (plusMinusNodeLost != null)
                        {
                            // For negative values
                            playerStat.PlusMinus = plusMinusNodeLost.InnerText.Trim();
                        }
                        else if (plusMinusNodeNeutral != null)
                        {
                            // For neutral values (0)
                            playerStat.PlusMinus = plusMinusNodeNeutral.InnerText.Trim();
                        }

                        // Player ADR
                        var adrNode = rowNode.SelectSingleNode(".//td[@class='newsitem-match-stats-adr']");
                        if (adrNode != null)
                        {
                            playerStat.ADR = adrNode.InnerText.Trim();
                        }

                        // Player Rating
                        var ratingNode = rowNode.SelectSingleNode(".//td[@class='newsitem-match-stats-rating']");
                        if (ratingNode != null)
                        {
                            playerStat.Rating = ratingNode.InnerText.Trim();
                        }

                        teamStats.PlayerStats.Add(playerStat);
                    }
                }

                return teamStats;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing team stats: {ex.Message}");
                return null;
            }
        }

        private void AddMatchResultToDocument(FlowDocument doc, MatchResult match, AppSettings settings)
        {
            // Get the appropriate text color based on dark mode setting
            Brush textColor = _isDarkMode 
                ? (SolidColorBrush)Application.Current.Resources["DarkModeText"] 
                : Brushes.Black;
                
            // Create a title with event name and match type
            Paragraph matchHeaderPara = new Paragraph()
            {
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(settings.ParagraphSideMargin, 10, settings.ParagraphSideMargin, 5),
                Foreground = textColor
            };
            matchHeaderPara.Inlines.Add(new Run($"{match.EventName} - {match.MatchType}"));
            doc.Blocks.Add(matchHeaderPara);

            // Create a table for the match result
            Table matchTable = new Table();
            matchTable.CellSpacing = 0;
            matchTable.Margin = new Thickness(settings.ParagraphSideMargin, 5, settings.ParagraphSideMargin, 10);

            // Add columns
            matchTable.Columns.Add(new TableColumn() { Width = new GridLength(3, GridUnitType.Star) }); // Team 1
            matchTable.Columns.Add(new TableColumn() { Width = new GridLength(1, GridUnitType.Star) }); // Score
            matchTable.Columns.Add(new TableColumn() { Width = new GridLength(3, GridUnitType.Star) }); // Team 2

            // Add row groups
            TableRowGroup rowGroup = new TableRowGroup();
            matchTable.RowGroups.Add(rowGroup);

            // Add team names and scores
            TableRow teamRow = new TableRow();

            TableCell team1Cell = new TableCell(new Paragraph(new Run(match.Team1Name))
            {
                TextAlignment = TextAlignment.Right,
                FontWeight = FontWeights.Bold,
                Foreground = textColor
            });

            TableCell scoreCell = new TableCell(new Paragraph(new Run($"{match.Team1Score} - {match.Team2Score}"))
            {
                TextAlignment = TextAlignment.Center,
                FontWeight = FontWeights.Bold,
                Foreground = textColor
            });

            TableCell team2Cell = new TableCell(new Paragraph(new Run(match.Team2Name))
            {
                TextAlignment = TextAlignment.Left,
                FontWeight = FontWeights.Bold,
                Foreground = textColor
            });

            teamRow.Cells.Add(team1Cell);
            teamRow.Cells.Add(scoreCell);
            teamRow.Cells.Add(team2Cell);
            rowGroup.Rows.Add(teamRow);

            // Add map results if available
            if (match.Maps.Count > 0)
            {
                foreach (var map in match.Maps)
                {
                    TableRow mapRow = new TableRow();

                    TableCell map1Cell = new TableCell(new Paragraph(new Run(map.Team1Score))
                    {
                        TextAlignment = TextAlignment.Right,
                        Foreground = textColor
                    });

                    TableCell mapNameCell = new TableCell(new Paragraph(new Run(map.MapName))
                    {
                        TextAlignment = TextAlignment.Center,
                        FontStyle = FontStyles.Italic,
                        Foreground = textColor
                    });

                    TableCell map2Cell = new TableCell(new Paragraph(new Run(map.Team2Score))
                    {
                        TextAlignment = TextAlignment.Left,
                        Foreground = textColor
                    });

                    mapRow.Cells.Add(map1Cell);
                    mapRow.Cells.Add(mapNameCell);
                    mapRow.Cells.Add(map2Cell);
                    rowGroup.Rows.Add(mapRow);
                }
            }

            // Add match date
            TableRow dateRow = new TableRow();
            TableCell dateLabelCell = new TableCell(new Paragraph()
            {
                TextAlignment = TextAlignment.Right,
                FontStyle = FontStyles.Italic,
                Foreground = textColor
            });

            TableCell dateValueCell = new TableCell(new Paragraph(new Run(match.MatchDate))
            {
                TextAlignment = TextAlignment.Center,
                FontStyle = FontStyles.Italic,
                FontSize = settings.ArticleFontSize * 0.9,
                Foreground = textColor
            });
            dateValueCell.ColumnSpan = 2;

            dateRow.Cells.Add(dateLabelCell);
            dateRow.Cells.Add(dateValueCell);
            rowGroup.Rows.Add(dateRow);

            doc.Blocks.Add(matchTable);
        }

        private void AddTeamStatsToDocument(FlowDocument doc, TeamStats stats, AppSettings settings)
        {
            // Get the appropriate text color based on dark mode setting
            Brush textColor = _isDarkMode 
                ? (SolidColorBrush)Application.Current.Resources["DarkModeText"] 
                : Brushes.Black;
                
            // Create a title with team name
            Paragraph teamNamePara = new Paragraph(new Run(stats.TeamName))
            {
                TextAlignment = TextAlignment.Center,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(settings.ParagraphSideMargin, 10, settings.ParagraphSideMargin, 5),
                Foreground = textColor
            };
            doc.Blocks.Add(teamNamePara);

            // Create a table for player stats
            Table statsTable = new Table();
            statsTable.CellSpacing = 0;
            statsTable.Margin = new Thickness(settings.ParagraphSideMargin, 5, settings.ParagraphSideMargin, 15);

            // Add columns
            statsTable.Columns.Add(new TableColumn() { Width = new GridLength(3, GridUnitType.Star) }); // Player
            statsTable.Columns.Add(new TableColumn() { Width = new GridLength(1, GridUnitType.Star) }); // K-D
            statsTable.Columns.Add(new TableColumn() { Width = new GridLength(1, GridUnitType.Star) }); // +/-
            statsTable.Columns.Add(new TableColumn() { Width = new GridLength(1, GridUnitType.Star) }); // ADR
            statsTable.Columns.Add(new TableColumn() { Width = new GridLength(1, GridUnitType.Star) }); // Rating

            // Add row groups
            TableRowGroup rowGroup = new TableRowGroup();
            statsTable.RowGroups.Add(rowGroup);

            // Add header row (keep header with dark text on light background for readability)
            TableRow headerRow = new TableRow();
            headerRow.Background = _isDarkMode ? Brushes.DimGray : Brushes.LightGray;

            headerRow.Cells.Add(new TableCell(new Paragraph(new Bold(new Run("Player")))
            {
                TextAlignment = TextAlignment.Left,
                Foreground = _isDarkMode ? Brushes.White : Brushes.Black
            }));

            headerRow.Cells.Add(new TableCell(new Paragraph(new Bold(new Run("K-D")))
            {
                TextAlignment = TextAlignment.Center,
                Foreground = _isDarkMode ? Brushes.White : Brushes.Black
            }));

            headerRow.Cells.Add(new TableCell(new Paragraph(new Bold(new Run("+/-")))
            {
                TextAlignment = TextAlignment.Center,
                Foreground = _isDarkMode ? Brushes.White : Brushes.Black
            }));

            headerRow.Cells.Add(new TableCell(new Paragraph(new Bold(new Run("ADR")))
            {
                TextAlignment = TextAlignment.Center,
                Foreground = _isDarkMode ? Brushes.White : Brushes.Black
            }));

            headerRow.Cells.Add(new TableCell(new Paragraph(new Bold(new Run("Rating")))
            {
                TextAlignment = TextAlignment.Center,
                Foreground = _isDarkMode ? Brushes.White : Brushes.Black
            }));

            rowGroup.Rows.Add(headerRow);

            // Add player stats
            foreach (var player in stats.PlayerStats)
            {
                TableRow playerRow = new TableRow();

                // Player name
                TableCell nameCell = new TableCell(new Paragraph()
                {
                    TextAlignment = TextAlignment.Left
                });
                if (!string.IsNullOrEmpty(player.Country))
                {
                    ((Paragraph)nameCell.Blocks.FirstBlock).Inlines.Add(new Run($"{player.Country} ") 
                    { 
                        Foreground = textColor
                    });
                }
                ((Paragraph)nameCell.Blocks.FirstBlock).Inlines.Add(new Bold(new Run(player.PlayerName)
                {
                    Foreground = textColor
                }));


                // K-D
                TableCell kdCell = new TableCell(new Paragraph(new Run(player.KD))
                {
                    TextAlignment = TextAlignment.Center,
                    Foreground = textColor
                });

                // +/-
                TableCell diffCell = new TableCell(new Paragraph()
                {
                    TextAlignment = TextAlignment.Center
                });
                
                // Apply appropriate color for +/- value
                if (player.PlusMinus.StartsWith("+"))
                {
                    // Positive values in green
                    ((Paragraph)diffCell.Blocks.FirstBlock).Inlines.Add(new Run(player.PlusMinus) 
                    { 
                        Foreground = Brushes.Green 
                    });
                }
                else if (player.PlusMinus.StartsWith("-"))
                {
                    // Negative values in red
                    ((Paragraph)diffCell.Blocks.FirstBlock).Inlines.Add(new Run(player.PlusMinus) 
                    { 
                        Foreground = Brushes.Red 
                    });
                }
                else
                {
                    // Neutral values (0) in default color
                    ((Paragraph)diffCell.Blocks.FirstBlock).Inlines.Add(new Run(player.PlusMinus)
                    {
                        Foreground = textColor
                    });
                }

                // ADR
                TableCell adrCell = new TableCell(new Paragraph(new Run(player.ADR))
                {
                    TextAlignment = TextAlignment.Center,
                    Foreground = textColor
                });

                // Rating
                TableCell ratingCell = new TableCell(new Paragraph(new Run(player.Rating))
                {
                    TextAlignment = TextAlignment.Center,
                    Foreground = textColor
                });

                playerRow.Cells.Add(nameCell);
                playerRow.Cells.Add(kdCell);
                playerRow.Cells.Add(diffCell);
                playerRow.Cells.Add(adrCell);
                playerRow.Cells.Add(ratingCell);
                rowGroup.Rows.Add(playerRow);
            }

            doc.Blocks.Add(statsTable);
        }
    }

    // Classes to represent match results and player stats
    public class MatchResult
    {
        public string EventName { get; set; } = "";
        public string MatchType { get; set; } = "";
        public string Team1Name { get; set; } = "";
        public string Team2Name { get; set; } = "";
        public string Team1Logo { get; set; } = "";
        public string Team2Logo { get; set; } = "";
        public string Team1Score { get; set; } = "";
        public string Team2Score { get; set; } = "";
        public string MatchDate { get; set; } = "";
        public List<MapResult> Maps { get; set; } = new List<MapResult>();
    }

    public class MapResult
    {
        public string MapName { get; set; } = "";
        public string Team1Score { get; set; } = "";
        public string Team2Score { get; set; } = "";
    }

    public class TeamStats
    {
        public string TeamName { get; set; } = "";
        public string TeamLogo { get; set; } = "";
        public List<PlayerStat> PlayerStats { get; set; } = new List<PlayerStat>();
    }

    public class PlayerStat
    {
        public string PlayerName { get; set; } = "";
        public string Country { get; set; } = "";
        public string KD { get; set; } = "";
        public string PlusMinus { get; set; } = "";
        public string ADR { get; set; } = "";
        public string Rating { get; set; } = "";
    }
}