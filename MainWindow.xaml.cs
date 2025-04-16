using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media; // Add this namespace for VisualTreeHelper
using System.Xml.Linq;
using HtmlAgilityPack; // added using directive. Ensure you install the HtmlAgilityPack NuGet package
using System.Windows.Documents; // added using directive for FlowDocument
using System.Windows.Media.Imaging; // Added for displaying images
using Nius.Settings; // Add reference to the settings namespace
using System.IO;
using System.Windows.Controls.Primitives; // For ToggleButton
using System.Windows.Threading;           // For DispatcherPriority

public class FeedItem
{
    public string Link { get; set; }
    public string ImageUrl { get; set; }
    public string PubDate { get; set; } // Added publication date property
    public BitmapImage LoadedImage { get; set; } // Loaded image

}

namespace Nius
{
    public partial class MainWindow : Window
    {
        // Replace individual settings with an AppSettings instance
        private AppSettings settings;
        private double currentTextBlockWidth;
        // NEW: Track article history persistently
        private bool showOpenedArticles = true; // NEW: true = show opened articles; false = hide them
        private ArticleHistory articleHistory = ArticleHistory.Load();
        // Add a class field to track the last selected article item
        private ListBoxItem lastSelectedArticleItem;
        private bool showArticleImages = true; // NEW: Toggle for image preview in list
        // Add a flag to track the current theme
        private bool _isDarkMode = false;

        public MainWindow()
        {
            InitializeComponent();

            // Load settings when the application starts
            settings = AppSettings.Load();

            // Load dark mode setting from settings
            _isDarkMode = settings.IsDarkMode;

            // Set up a handler for window size changes
            SizeChanged += MainWindow_SizeChanged;

            // Calculate initial width
            UpdateTextBlockWidth();

            // Initialize the article background with error handling - do this in the background
            Task.Run(() => Dispatcher.Invoke(() => LoadArticleBackground()));

            // Handle the Loaded event
            Loaded += MainWindow_Loaded;

            // NEW: Auto-focus header when selection changes
            NewsList.SelectionChanged += NewsList_SelectionChanged;
        }

        // Replace Window_Loaded with MainWindow_Loaded
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Debug.WriteLine("Window loaded, initializing application...");

                // Prune outdated article history entries during startup
                articleHistory.PruneOldEntries(30); // Remove entries older than 30 days

                // Apply saved settings immediately
                ApplySettingsToArticle();


                AppendStatus("Loading feeds...");

                // Give UI time to render
                await Task.Delay(100);

                // Now load the feeds
                await LoadFeeds();

                // NEW: Select the News tab and focus the list so keybindings work
                MainTabControl.SelectedIndex = 1;
                NewsList.Focus();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during startup: {ex}");
                MessageBox.Show($"Error during startup: {ex.Message}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                AppendStatus("Error during startup.");
            }
        }

        // Modify LoadArticleBackground to be more efficient
        private void LoadArticleBackground()
        {
            try
            {
                // Try a simpler approach - just use a direct path
                string imagePath = "img/beige-paper-texture.jpg";

                try
                {
                    if (File.Exists(imagePath))
                    {
                        BitmapImage bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad; // Load the image immediately
                        bitmap.UriSource = new Uri(imagePath, UriKind.Relative);
                        bitmap.EndInit();

                        ArticleBackground.ImageSource = bitmap;
                        return;
                    }
                }
                catch (Exception)
                {
                    // Fail silently and try the fallback
                }

                // Fall back to a solid color
                ArticleRichTextBox.Background = new SolidColorBrush(Color.FromRgb(249, 246, 238)); // Light beige color
            }
            catch (Exception)
            {
                ArticleRichTextBox.Background = Brushes.White;
            }
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Update width when window size changes
            UpdateTextBlockWidth();
            // Update any existing items if needed
            UpdateExistingTextBlockWidths();
            // Update article width to fit window
            UpdateArticleWidth();
        }

        private void UpdateTextBlockWidth() =>
            currentTextBlockWidth = this.ActualWidth * 0.8;

        private void UpdateExistingTextBlockWidths()
        {
            // Find all TextBlocks in the news list and update their widths
            foreach (var item in NewsList.Items)
            {
                if (item is Expander expander)
                {
                    // Update the header TextBlock
                    if (expander.Header is TextBlock headerTextBlock)
                    {
                        headerTextBlock.Width = currentTextBlockWidth;
                    }

                    // Update TextBlocks in article items
                    if (expander.Content is ListBox listBox)
                    {
                        foreach (var listItem in listBox.Items)
                        {
                            if (listItem is ListBoxItem lbi && lbi.Content is StackPanel panel)
                            {
                                foreach (var child in panel.Children)
                                {
                                    if (child is TextBlock textBlock)
                                    {
                                        textBlock.Width = currentTextBlockWidth;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private async Task LoadFeeds()
        {
            try
            {
                Debug.WriteLine("Starting to load feeds...");

                var feedUrls = GetFeedUrlsFromSettings();
                AppendStatus($"Loading {feedUrls.Count} unique feeds...");

                // Clear the NewsList before loading new items
                NewsList.Items.Clear();

                // Load and process feeds
                var feedDocuments = await FeedLoader.FetchFeedsAsync(feedUrls);
                AppendStatus($"Fetched {feedDocuments.Count} feed documents");

                if (feedDocuments.Count == 0)
                {
                    HandleEmptyFeedsList();
                    return;
                }

                // Process feeds and add to UI
                await ProcessFeedDocuments(feedDocuments, feedUrls);

                // Finalize the UI setup
                FinalizeFeedsUI();

                AppendStatus("Feeds loaded successfully.");
            }
            catch (Exception ex)
            {
                HandleFeedLoadingError(ex);
            }
        }

        /// <summary>
        /// Gets unique feed URLs from settings, adding defaults if needed
        /// </summary>
        private List<string> GetFeedUrlsFromSettings()
        {
            var feedUrls = settings.FeedUrls.Distinct().ToList();

            if (feedUrls.Count == 0)
            {
                AppendStatus("No feed URLs configured. Adding default feeds.");
                feedUrls.Add("https://taz.de/!p4608;rss/");
                feedUrls.Add("https://www.tagesschau.de/inland/index~rss2.xml");
                settings.FeedUrls = feedUrls;
                settings.Save();
            }

            return feedUrls;
        }

        /// <summary>
        /// Adds a placeholder when no feeds are available
        /// </summary>
        private void HandleEmptyFeedsList()
        {
            var noFeedsExpander = new Expander
            {
                Header = new TextBlock
                {
                    Text = "No feeds available",
                    Foreground = Brushes.Red,
                    FontWeight = FontWeights.Bold
                },
                IsExpanded = true
            };
            NewsList.Items.Add(noFeedsExpander);
            AppendStatus("No feed documents available.");
        }

        /// <summary>
        /// Processes each feed document and adds its items to the UI
        /// </summary>
        private Task ProcessFeedDocuments(List<XDocument> feedDocuments, List<string> feedUrls)
        {
            int globalArticleCounter = 1;
            int feedCounter = 0;
            XNamespace media = "http://search.yahoo.com/mrss/";
            XNamespace content = "http://purl.org/rss/1.0/modules/content/";

            var feedItems = new List<FeedItem>();

            foreach (var doc in feedDocuments)
            {
                try
                {
                    if (doc?.Root == null)
                    {
                        AppendStatus($"Skipping feed {feedCounter}: Empty or invalid XML");
                        feedCounter++;
                        continue;
                    }

                    // Get feed metadata
                    var channelInfo = ExtractChannelInfo(doc, feedUrls, feedCounter);

                    // Count unread articles
                    var (totalArticles, unreadCount) = CountUnreadArticles(doc);

                    // Create feed UI elements
                    var feedExpander = CreateFeedExpander(channelInfo, unreadCount);
                    var feedArticlesList = CreateFeedArticlesList();

                    // Process articles
                    bool hasArticles = ProcessFeedArticles(
                        doc, media, content, ref globalArticleCounter,
                        feedArticlesList, feedItems
                    );

                    // Add to UI if it has articles
                    if (hasArticles)
                    {
                        feedExpander.Content = feedArticlesList;
                        NewsList.Items.Add(feedExpander);
                    }

                    feedCounter++;
                }
                catch (Exception ex)
                {
                    AppendStatus($"Error processing feed {feedCounter}: {ex.Message}");
                    feedCounter++;
                }
            }

            AppendStatus($"Added {NewsList.Items.Count} feeds to UI");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Extracts channel metadata from a feed document
        /// </summary>
        private (string title, string link, string pubDate) ExtractChannelInfo(XDocument doc, List<string> feedUrls, int feedCounter)
        {
            XElement channel = doc.Root.Element("channel");
            string channelTitle = "Unknown Feed";
            string channelLink = feedUrls.Count > feedCounter ? feedUrls[feedCounter] : "No link";
            string channelPubDate = "Unknown date";

            if (channel != null)
            {
                channelTitle = channel.Element("title")?.Value?.Trim() ?? "No title";
                channelLink = channel.Element("link")?.Value?.Trim() ?? channelLink;
                channelPubDate = channel.Element("pubDate")?.Value?.Trim() ?? "No pubDate";
                AppendStatus($"Loaded feed: {channelTitle} ({channelLink})");
            }
            else
            {
                AppendStatus($"No channel metadata found in feed {feedCounter}");
            }

            return (channelTitle, channelLink, channelPubDate);
        }

        /// <summary>
        /// Counts total and unread articles in a feed
        /// </summary>
        private (int total, int unread) CountUnreadArticles(XDocument doc)
        {
            int totalArticles = 0;
            int unreadCount = 0;

            foreach (XElement item in doc.Descendants("item"))
            {
                string title = item.Element("title")?.Value.Trim();
                if (string.IsNullOrEmpty(title))
                    continue;

                totalArticles++;
                string link = item.Element("link")?.Value.Trim();
                if (string.IsNullOrEmpty(link))
                    link = item.Element("guid")?.Value.Trim();

                if (!articleHistory.IsOpened(link))
                    unreadCount++;
            }

            return (totalArticles, unreadCount);
        }

        /// <summary>
        /// Creates the feed expander UI element with header
        /// </summary>
        private Expander CreateFeedExpander((string title, string link, string pubDate) channelInfo, int unreadCount)
        {
            // Create header panel with title
            StackPanel headerPanel = new StackPanel();

            // Create title row with channel name and unread count
            StackPanel titleRow = new StackPanel { Orientation = Orientation.Horizontal };

            // Create the title with distinctive color
            TextBlock titleBlock = new TextBlock
            {
                Text = channelInfo.title,
                Foreground = System.Windows.Media.Brushes.Yellow,
                FontWeight = FontWeights.Bold,
                FontSize = 20,
                Margin = new Thickness(0, 0, 5, 5),
                TextWrapping = TextWrapping.Wrap
            };
            titleRow.Children.Add(titleBlock);

            // Add unread count if there are any
            if (unreadCount > 0)
            {
                TextBlock countBlock = new TextBlock
                {
                    Text = $"({unreadCount} unread)",
                    Foreground = System.Windows.Media.Brushes.LightGreen,
                    FontWeight = FontWeights.Bold,
                    FontSize = 16,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 0, 0, 0)
                };
                titleRow.Children.Add(countBlock);
            }

            headerPanel.Children.Add(titleRow);

            // Add link and publish date with original cyan color
            TextBlock metadataBlock = new TextBlock
            {
                Foreground = System.Windows.Media.Brushes.Cyan,
                Margin = new Thickness(0, 0, 0, 5),
                TextWrapping = TextWrapping.Wrap,
                Width = currentTextBlockWidth
            };
            metadataBlock.Text = $"{channelInfo.link}\nPublished: {channelInfo.pubDate}";
            headerPanel.Children.Add(metadataBlock);

            // Create an Expander for this feed
            Expander feedExpander = new Expander
            {
                Header = headerPanel,
                IsExpanded = false,
                Focusable = true,
                IsTabStop = true,
                Tag = channelInfo.link // Store the feed URL in the Tag
            };

            // Use PreviewKeyDown to handle keys at a lower level
            feedExpander.PreviewKeyDown += FeedExpander_PreviewKeyDown;
            feedExpander.Expanded += FeedExpander_Expanded;

            return feedExpander;
        }

        /// <summary>
        /// Creates a ListBox for feed articles with proper styling and event handlers
        /// </summary>
        private ListBox CreateFeedArticlesList()
        {
            ListBox feedArticlesList = new ListBox
            {
                Background = System.Windows.Media.Brushes.Black,
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                FontFamily = new System.Windows.Media.FontFamily("Ubuntu Mono")
            };

            // Set attached properties
            KeyboardNavigation.SetTabNavigation(feedArticlesList, KeyboardNavigationMode.Once);
            KeyboardNavigation.SetDirectionalNavigation(feedArticlesList, KeyboardNavigationMode.Cycle);

            feedArticlesList.KeyDown += FeedArticlesList_KeyDown;

            VirtualizingStackPanel.SetIsVirtualizing(feedArticlesList, true);
            VirtualizingStackPanel.SetVirtualizationMode(feedArticlesList, VirtualizationMode.Recycling);

            return feedArticlesList;
        }

        /// <summary>
        /// Processes articles from a feed document and adds them to the ListBox
        /// </summary>
        private bool ProcessFeedArticles(
            XDocument doc,
            XNamespace media,
            XNamespace content,
            ref int globalArticleCounter,
            ListBox feedArticlesList,
            List<FeedItem> feedItems)
        {
            bool hasArticles = false;

            foreach (XElement item in doc.Descendants("item"))
            {
                string title = item.Element("title")?.Value.Trim();
                if (string.IsNullOrEmpty(title))
                    continue;

                var feedItem = ExtractFeedItem(item, media, content);
                feedItems.Add(feedItem);

                // Skip if hiding opened articles and this one is already read
                if (!showOpenedArticles && articleHistory.IsOpened(feedItem.Link))
                    continue;

                // Create the article's ListBoxItem
                var articleItem = CreateArticleListBoxItem(feedItem, title, globalArticleCounter++);
                feedArticlesList.Items.Add(articleItem);
                hasArticles = true;
            }

            return hasArticles;
        }

        /// <summary>
        /// Creates a ListBoxItem for an article with proper content and event handlers
        /// </summary>
        private ListBoxItem CreateArticleListBoxItem(FeedItem feedItem, string title, int articleIndex)
        {
            // Create a horizontal panel to contain preview image + texts
            var itemPanel = new StackPanel { Orientation = Orientation.Horizontal };

            // Add image if enabled and available
            if (showArticleImages && !string.IsNullOrEmpty(feedItem.ImageUrl))
            {
                Image previewImage = new Image
                {
                    Width = 50,
                    Height = 50,
                    Margin = new Thickness(0, 0, 5, 0),
                    Source = new BitmapImage(new Uri("pack://application:,,,/img/placeholder.jpg", UriKind.Absolute))
                };
                itemPanel.Children.Add(previewImage);
            }

            // Create a vertical panel for text info
            var textPanel = new StackPanel { Orientation = Orientation.Vertical };

            // Date string in smaller font
            textPanel.Children.Add(new TextBlock
            {
                Text = feedItem.PubDate,
                FontSize = 12,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 2),
                Width = currentTextBlockWidth
            });

            // Headline TextBlock
            var headlineTB = new TextBlock
            {
                Text = $"{articleIndex}. {title}",
                TextWrapping = TextWrapping.Wrap,
                Width = currentTextBlockWidth
            };

            // Determine the correct foreground color based on article status
            if (articleHistory.IsOpened(feedItem.Link))
            {
                // Article has been read
                headlineTB.Foreground = Brushes.Gray;
            }
            else if (!articleHistory.HasBeenDisplayed(feedItem.Link))
            {
                // This is a brand new article (never displayed before)
                headlineTB.Foreground = Brushes.LightGreen;
            }
            else
            {
                // Unread but previously displayed article
                headlineTB.Foreground = Brushes.White;
            }

            textPanel.Children.Add(headlineTB);
            textPanel.Children.Add(new Separator { Margin = new Thickness(0, 6, 0, 6) });

            itemPanel.Children.Add(textPanel);

            // Create the ListBoxItem and attach events
            var articleItem = new ListBoxItem { Content = itemPanel, Tag = feedItem };

            // Create and attach context menu
            SetupArticleContextMenu(articleItem, feedItem);

            // Attach event handlers
            AttachArticleEventHandlers(articleItem, feedItem);

            return articleItem;
        }

        /// <summary>
        /// Sets up the context menu for an article item
        /// </summary>
        private void SetupArticleContextMenu(ListBoxItem articleItem, FeedItem feedItem)
        {
            ContextMenu contextMenu = new ContextMenu();

            // Add "Mark as Read" menu item
            MenuItem markAsReadItem = new MenuItem { Header = "Mark as Read" };
            markAsReadItem.Click += (s, e) =>
            {
                MarkArticleAsRead(feedItem);
                e.Handled = true;
            };
            contextMenu.Items.Add(markAsReadItem);

            // Add "Copy Link" menu item
            MenuItem copyLinkItem = new MenuItem { Header = "Copy Link" };
            copyLinkItem.Click += (s, e) =>
            {
                CopyArticleLink(feedItem);
                e.Handled = true;
            };
            contextMenu.Items.Add(copyLinkItem);

            articleItem.ContextMenu = contextMenu;
        }

        /// <summary>
        /// Attaches event handlers to an article item
        /// </summary>
        private void AttachArticleEventHandlers(ListBoxItem articleItem, FeedItem feedItem)
        {
            // Handle double-click
            articleItem.PreviewMouseDoubleClick += (s, e) =>
            {
                HandleLinkClick(feedItem);
                e.Handled = true;
            };

            // Handle keyboard navigation
            articleItem.KeyDown += (s, e) =>
            {
                switch (e.Key)
                {
                    case Key.Enter:
                    case Key.Right:
                        // Open article
                        HandleLinkClick(feedItem);
                        e.Handled = true;
                        break;

                    case Key.Left:
                        // Find and collapse parent feed
                        var parentListBox = FindVisualParent<ListBox>(articleItem);
                        var parentExpander = FindVisualParent<Expander>(parentListBox);
                        if (parentExpander != null)
                        {
                            parentExpander.IsExpanded = false;
                            parentExpander.Focus();
                            e.Handled = true;
                        }
                        break;
                }
            };
        }

        /// <summary>
        /// Final UI setup after loading feeds
        /// </summary>
        private void FinalizeFeedsUI()
        {
            // Focus the first feed if available
            if (NewsList.Items.Count > 0)
            {
                if (NewsList.Items[0] is Expander firstExpander)
                {
                    firstExpander.Focus();
                }
            }

            // Update unread counts across all feeds
            UpdateUnreadCounts();

            // Ensure list is properly synchronized
            NewsList.IsSynchronizedWithCurrentItem = true;
        }

        /// <summary>
        /// Handles errors during feed loading
        /// </summary>
        private void HandleFeedLoadingError(Exception ex)
        {
            Debug.WriteLine($"Critical error in LoadFeeds: {ex}");
            MessageBox.Show($"Error loading feeds: {ex.Message}", "Feed Error", MessageBoxButton.OK, MessageBoxImage.Error);

            // Add a placeholder to show something in the UI
            NewsList.Items.Clear();
            var errorItem = new ListBoxItem
            {
                Content = new TextBlock
                {
                    Text = $"Error loading feeds: {ex.Message}\nPlease check your internet connection.",
                    Foreground = Brushes.Red,
                    TextWrapping = TextWrapping.Wrap
                }
            };
            NewsList.Items.Add(errorItem);
            AppendStatus("Error loading feeds.");
        }

        private async Task LoadImagesAsync(IEnumerable<FeedItem> feedItems)
        {
            foreach (var item in feedItems)
            {
                if (!string.IsNullOrEmpty(item.ImageUrl))
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(item.ImageUrl, UriKind.Absolute);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        bitmap.Freeze(); // Freeze the image for thread safety

                        // Update the LoadedImage property
                        item.LoadedImage = bitmap;

                        // Update the UI
                        await Dispatcher.InvokeAsync(() =>
                        {
                            NewsList.Items.Refresh();
                        });
                    }
                    catch
                    {
                        // Handle image loading errors
                    }
                }
            }
        }

        private void FeedArticlesList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Get the ListBox that received the event
            if (sender is ListBox feedArticlesList)
            {
                ScrollViewer scrollViewer = FindVisualChild<ScrollViewer>(feedArticlesList);
                if (scrollViewer != null)
                {
                    // Calculate the offset distance based on mouse wheel delta
                    double scrollAmount = 30 * settings.MouseWheelScrollLines; // Use settings value
                    if (e.Delta > 0)
                    {
                        // Smooth scrolling up
                        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - scrollAmount);
                    }
                    else
                    {
                        // Smooth scrolling down
                        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + scrollAmount);
                    }
                    e.Handled = true; // Mark the event as handled to prevent it from bubbling up
                }
            }
        }

        // Helper method to update formatting of the currently displayed article.
        private void UpdateArticleFormat()
        {
            FlowDocument doc = ArticleRichTextBox.Document;
            if (doc != null)
            {
                // Apply settings from the settings object
                doc.FontSize = settings.ArticleFontSize;
                doc.FontFamily = new FontFamily(settings.ArticleFontFamily);
                doc.PagePadding = new Thickness(20); // Keep the page padding as is

                // Make sure the background is transparent so the paper texture shows through
                doc.Background = Brushes.Transparent;

                // Set optimal width for no horizontal scrolling
                UpdateArticleWidth();

                // Update margins for all Paragraphs.
                foreach (Block block in doc.Blocks)
                {
                    if (block is Paragraph para)
                    {
                        para.Margin = new Thickness(settings.ParagraphSideMargin, 0, settings.ParagraphSideMargin, settings.ArticleMargin);
                        para.TextAlignment = TextAlignment.Justify; // Better text formatting

                        // Adjust the text color to be darker to ensure good contrast with paper
                        para.Foreground = Brushes.Black;
                    }
                }
                Debug.WriteLine($"Updated article format: FontSize={settings.ArticleFontSize}, BottomMargin={settings.ArticleMargin}, SideMargin={settings.ParagraphSideMargin}");

                // Always save settings after format changes
                settings.Save();
            }
        }

        // Add this method to the MainWindow class to update the article view when window size changes
        private void UpdateArticleWidth()
        {
            if (ArticleRichTextBox?.Document != null)
            {
                // Calculate margins based on window width
                double windowWidth = this.ActualWidth;
                double contentWidth = windowWidth - (2 * settings.ParagraphSideMargin) - 40; // account for padding and scrollbar

                // Update the FlowDocument's PageWidth
                ArticleRichTextBox.Document.PageWidth = contentWidth;

                // Update paragraph widths for proper text wrapping
                foreach (Block block in ArticleRichTextBox.Document.Blocks)
                {
                    if (block is Paragraph para)
                    {
                        para.TextAlignment = TextAlignment.Justify;
                    }
                }

                Debug.WriteLine($"Updated article width to {contentWidth}px");
            }
        }



        // New overloaded HandleLinkClick that takes a FeedItem.
        private async void HandleLinkClick(FeedItem feedData)
        {
            // Find and store the current ListBoxItem before navigating
            if (FocusManager.GetFocusedElement(this) is ListBoxItem item && item.Tag is FeedItem)
            {
                lastSelectedArticleItem = item;
            }

            if (feedData == null || string.IsNullOrEmpty(feedData.Link))
            {
                Debug.WriteLine("No link available to open.");
                return;
            }

            try
            {
                // Use FetchArticleAsync from FeedLoader instead of a new HttpClient
                string content = await FeedLoader.FetchArticleAsync(feedData.Link);
                Debug.WriteLine("Article loaded from " + feedData.Link);
                AppendStatus("Article loaded from " + feedData.Link);
                // File.WriteAllText("debug_output.txt", content);

                FlowDocument doc;
                if (feedData.Link.Contains("tagesschau.de"))
                {
                    string debugText = "Parsing with Tagesschau parser.";
                    Debug.WriteLine(debugText);
                    AppendStatus(debugText);
                    doc = await ArticleParser.ParseTagesschauArticle(content, feedData, settings, this.ActualWidth);
                }
                else if (feedData.Link.Contains("hltv.org"))
                {
                    string debugText = "Parsing with HLTV parser.";
                    Debug.WriteLine(debugText);
                    AppendStatus(debugText);
                    doc = await ArticleParser.ParseHLTVArticle(content, feedData, settings, this.ActualWidth);
                }
                else if (feedData.Link.Contains("spiegel.de"))
                {
                    string debugText = "Parsing with Spiegel parser.";
                    Debug.WriteLine(debugText);
                    AppendStatus(debugText);
                    doc = await ArticleParser.ParseSpiegelArticle(content, feedData, settings, this.ActualWidth);
                }
                else
                {
                    string debugText = "Parsing with Taz parser.";
                    Debug.WriteLine(debugText);
                    AppendStatus(debugText);
                    doc = await ArticleParser.ParseTazArticle(content, feedData, settings, this.ActualWidth);
                }

                ArticleRichTextBox.Document = doc;

                // Apply dark mode to the article document if we're in dark mode
                if (_isDarkMode)
                {
                    // Apply dark background and beige text to FlowDocument
                    doc.Background = (SolidColorBrush)Application.Current.Resources["DarkModeBackground"];

                    // Update text color for all paragraphs
                    foreach (Block block in doc.Blocks)
                    {
                        if (block is Paragraph para)
                        {
                            para.Foreground = (SolidColorBrush)Application.Current.Resources["DarkModeText"];
                        }
                    }
                }

                MainTabControl.SelectedIndex = 2; // switch to Article tab
                ArticleScrollViewer.Focus();
                ArticleScrollViewer.ScrollToTop();

                // Just call MarkArticleAsRead which handles all style updates in one place
                MarkArticleAsRead(feedData);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error loading article: " + ex.Message);
                AppendStatus("Error loading article: " + ex.Message);
                // Create a better error document
                FlowDocument errorDoc = new FlowDocument();
                Paragraph errorTitle = new Paragraph(new Run("Error Loading Article"))
                {
                    FontWeight = FontWeights.Bold,
                    FontSize = 16
                };
                Paragraph errorMsg = new Paragraph(new Run(ex.Message));

                errorDoc.Blocks.Add(errorTitle);
                errorDoc.Blocks.Add(errorMsg);

                ArticleRichTextBox.Document = errorDoc;
            }
        }

        // Updated Event Handlers to extract the FeedItem directly from sender.
        private void ListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && item.Tag is FeedItem feedData)
            {
                Debug.WriteLine("Article loading initiated via double click. Link: " + feedData.Link);
                HandleLinkClick(feedData);
            }
        }

        private void ListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Right)
            {
                if (sender is ListBoxItem item && item.Tag is FeedItem feedData)
                {
                    Debug.WriteLine("Article loading initiated via " + e.Key + " key from list view. Link: " + feedData.Link);
                    HandleLinkClick(feedData);
                }
                e.Handled = true;
            }
        }

        private void MainTabControl_KeyDown(object sender, KeyEventArgs e)
        {
            // When in the article tab, Left key should go back to news tab.
            if (e.Key == Key.Left && MainTabControl.SelectedIndex == 2)
            {
                MainTabControl.SelectedIndex = 1;
                e.Handled = true;

                // Try to restore focus to the last selected item
                if (lastSelectedArticleItem != null)
                {
                    lastSelectedArticleItem.Focus();
                }
                else
                {
                    NewsList.Focus();
                }
            }
            // Optionally, additional navigation can be added here.
        }

        private void ArticleRichTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Left)
            {
                MainTabControl.SelectedIndex = 0; // Switch back to News tab
                e.Handled = true;
            }
        }

        // Use the ScrollViewer's PreviewKeyDown to intercept dynamic formatting hotkeys.
        private void ArticleScrollViewer_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            Debug.WriteLine($"Key pressed: {e.Key}, Modifiers: {Keyboard.Modifiers}");

            // If left key alone, switch tab.
            if (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.None && MainTabControl.SelectedIndex == 2)
            {
                MainTabControl.SelectedIndex = 1;
                e.Handled = true;

                // Try to restore focus to the last selected item
                if (lastSelectedArticleItem != null)
                {
                    lastSelectedArticleItem.Focus();
                }
                else
                {
                    NewsList.Focus();
                }
                return;
            }

            // Handle formatting shortcuts in a centralized way
            if ((e.Key == Key.Add || e.Key == Key.OemPlus || e.Key == Key.Subtract || e.Key == Key.OemMinus) &&
                (Keyboard.Modifiers != ModifierKeys.None))
            {
                HandleFormatShortcut(e.Key, Keyboard.Modifiers);
                e.Handled = true;
                return;
            }

            // ...existing code for other key handling...
        }

        private void NewsList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ScrollViewer scrollViewer = FindVisualChild<ScrollViewer>(NewsList);
            if (scrollViewer != null)
            {
                // Calculate the offset distance based on mouse wheel delta
                double scrollAmount = 30 * settings.MouseWheelScrollLines; // Use settings value
                if (e.Delta > 0)
                {
                    // Smooth scrolling up
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - scrollAmount);
                }
                else
                {
                    // Smooth scrolling down
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + scrollAmount);
                }
            }
        }

        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child != null && child is T)
                    return (T)child;

                T childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }
            return null;
        }

        private void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            // Show loading status
            AppendStatus("Reloading feeds...");

            // Use async/await pattern properly
            _ = Task.Run(async () =>
            {
                // Fetch data on background thread
                var feedUrls = GetFeedUrlsFromSettings();
                var feedDocuments = await FeedLoader.FetchFeedsAsync(feedUrls);

                // Switch back to UI thread to update UI elements
                await Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        // Clear the news list on UI thread
                        NewsList.Items.Clear();

                        if (feedDocuments.Count == 0)
                        {
                            HandleEmptyFeedsList();
                            return;
                        }

                        // Process feeds and add to UI on UI thread
                        await ProcessFeedDocuments(feedDocuments, feedUrls);

                        // Finalize the UI setup
                        FinalizeFeedsUI();

                        AppendStatus("Feeds loaded successfully.");
                    }
                    catch (Exception ex)
                    {
                        HandleFeedLoadingError(ex);
                    }
                });
            });
        }

        // Replace both existing ToggleOpenedButton_Click methods with this one
        private void ToggleOpenedButton_Click(object sender, RoutedEventArgs e)
        {
            showOpenedArticles = !showOpenedArticles;
            Debug.WriteLine($"Toggled showOpenedArticles to {showOpenedArticles}");

            // Try to update visibility without full reload if possible
            if (TryRefreshNewsListVisibility())
            {
                // If the list was successfully filtered without reload
                Debug.WriteLine("Updated article visibility without reloading feeds");
            }
            else
            {
                // Fall back to full reload if filtering doesn't work
                // Use Task.Run to avoid blocking the UI thread
                // and don't make the method async since we don't need to await
                Task.Run(async () => await LoadFeeds());
            }
        }

        // Add this helper method to attempt filtering without reload
        private bool TryRefreshNewsListVisibility()
        {
            bool success = false;
            try
            {
                foreach (var expanderObj in NewsList.Items)
                {
                    if (expanderObj is Expander expander && expander.Content is ListBox listBox)
                    {
                        foreach (var itemObj in listBox.Items)
                        {
                            if (itemObj is ListBoxItem lbi && lbi.Tag is FeedItem feedItem)
                            {
                                bool isOpened = articleHistory.IsOpened(feedItem.Link);
                                bool shouldBeVisible = showOpenedArticles || !isOpened;

                                // Update visibility based on opened status and current filter setting
                                lbi.Visibility = shouldBeVisible ? Visibility.Visible : Visibility.Collapsed;
                            }
                        }
                        success = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error filtering articles: {ex.Message}");
                success = false;
            }

            return success;
        }

        // NEW: Update styles immediately (called after marking an article opened)
        private void UpdateOpenedStyles()
        {
            // Create a darker gray brush matching the ReadItemStyle
            SolidColorBrush readItemBrush = (SolidColorBrush)FindResource("ReadItemBrush") ??
                                           new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)); //rgb(130, 130, 130) - darker gray

            foreach (var expanderObj in NewsList.Items)
            {
                if (expanderObj is Expander expander &&
                    expander.Content is ListBox listBox)
                {
                    foreach (var itemObj in listBox.Items)
                    {
                        if (itemObj is ListBoxItem lbi && lbi.Tag is FeedItem feedItem)
                        {
                            // Find the headline TextBlock inside the item's panel
                            if (lbi.Content is StackPanel panel)
                            {
                                foreach (var child in panel.Children)
                                {
                                    if (child is TextBlock tb && tb.Text.Contains(". "))
                                    {
                                        tb.Foreground = articleHistory.IsOpened(feedItem.Link)
                                            ? readItemBrush  // Use the darker gray brush instead of LightGray
                                            : Brushes.White;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ScrollViewer scrollViewer = sender as ScrollViewer;
            if (scrollViewer != null)
            {
                // Calculate the offset distance based on mouse wheel delta and settings
                double scrollAmount = 30 * settings.MouseWheelScrollLines;

                if (e.Delta < 0)
                {
                    // Smooth scrolling down using settings
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + scrollAmount);
                }
                else
                {
                    // Smooth scrolling up using settings
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - scrollAmount);
                }

                e.Handled = true;
            }
        }

        // Handle key navigation in feed article lists to prevent bubbling
        private void FeedArticlesList_KeyDown(object sender, KeyEventArgs e)
        {
            ListBox listBox = sender as ListBox;
            if (listBox == null) return;

            if (e.Key == Key.Enter || e.Key == Key.Right)
            {
                // Open article with Enter or Right when a ListBoxItem is selected
                if (listBox.SelectedItem is ListBoxItem selectedItem &&
                    selectedItem.Tag is FeedItem feedItem)
                {
                    HandleLinkClick(feedItem);
                    e.Handled = true; // Stop event bubbling
                    return; // Exit early after handling the article click
                }
            }
            else if (e.Key == Key.Left)
            {
                // Left key should collapse parent feed and focus header
                Expander parentExpander = FindVisualParent<Expander>(listBox);
                if (parentExpander != null)
                {
                    // Collapse the expander
                    parentExpander.IsExpanded = false;

                    // Focus the header
                    parentExpander.Focus();
                    e.Handled = true;
                }
            }
        }

        // Improve expander key handling to coordinate with list items
        private void FeedExpander_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!(sender is Expander expander)) return;

            // Get the original source of the event to ensure it's directly from the expander
            if (e.OriginalSource != sender && !(e.OriginalSource is ToggleButton))
            {
                // Event came from a child element, not directly from expander header
                return;
            }

            switch (e.Key)
            {
                case Key.Enter:
                case Key.Right: // Make Right key behave like Enter
                    // Toggle expanded state on Enter or Right key
                    expander.IsExpanded = !expander.IsExpanded;
                    e.Handled = true;
                    break;
            }
        }

        // Modify FeedExpander_Expanded to ensure proper focus transfer
        private void FeedExpander_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is Expander expander && expander.Content is ListBox listBox && listBox.Items.Count > 0)
            {
                // Give the UI a moment to update before setting focus
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    listBox.Focus();
                    listBox.SelectedIndex = 0;

                    // Make sure the UI is updated before trying to focus the item
                    listBox.UpdateLayout();

                    if (listBox.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                    {
                        var item = listBox.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
                        item?.Focus();
                    }

                    // Make sure the UI is updated before trying to focus the item
                    listBox.UpdateLayout();

                    if (listBox.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                    {
                        var item = listBox.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
                        item?.Focus();
                    }

                    // Scroll the header (Expander) into view
                    ScrollViewer scrollViewer = FindVisualChild<ScrollViewer>(NewsList);
                    if (scrollViewer != null)
                    {
                        // Use BringIntoView to ensure the Expander header is visible
                        expander.BringIntoView();

                        // Alternatively, calculate the position manually
                        GeneralTransform transform = expander.TransformToAncestor(scrollViewer);
                        Point position = transform.Transform(new Point(0, 0));
                        scrollViewer.ScrollToVerticalOffset(position.Y);
                    }
                }), DispatcherPriority.ContextIdle);
            }
        }

        // --- New helper: extract FeedItem from an XElement ---
        private FeedItem ExtractFeedItem(XElement item, XNamespace media, XNamespace content)
        {
            string title = item.Element("title")?.Value.Trim();
            string link = item.Element("link")?.Value.Trim();
            if (string.IsNullOrEmpty(link))
            {
                link = item.Element("guid")?.Value.Trim();
                Debug.WriteLine($"Link not found in <link>; using <guid>: {link}");
            }
            else Debug.WriteLine("Found link: " + link);

            // Get image from content:encoded, if available
            string imageUrl = null;
            var contentEncoded = item.Element(content + "encoded");
            if (contentEncoded != null)
            {
                var htmlDoc = new HtmlAgilityPack.HtmlDocument();
                htmlDoc.LoadHtml(contentEncoded.Value);
                var imgNode = htmlDoc.DocumentNode.SelectSingleNode("//img");
                imageUrl = imgNode?.GetAttributeValue("src", null);
            }
            if (string.IsNullOrEmpty(imageUrl))
            {
                var mediaContent = item.Element(media + "content");
                imageUrl = mediaContent?.Attribute("url")?.Value;
            }

            string pubDate = item.Element("pubDate")?.Value.Trim() ?? "No date available";

            return new FeedItem { Link = link, ImageUrl = imageUrl, PubDate = pubDate };
        }

        // Enhanced ListBox keyboard navigation at the main list level
        // Updated NewsList_PreviewKeyDown: only intercepts Enter/Right, leaves Up/Down for default navigation.
        private void NewsList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            ListBox listBox = sender as ListBox;
            if (listBox == null) return;

            // Let Up and Down keys work normally.
            if (e.Key == Key.Up || e.Key == Key.Down)
                return;

            // If a ListBoxItem (article headline) has focus, let its own handler take over.
            if (FocusManager.GetFocusedElement(this) is ListBoxItem)
                return;

            // Only process Enter/Right if the selected item is an Expander.
            if (e.Key == Key.Enter || e.Key == Key.Right)
            {
                if (listBox.SelectedItem is Expander expander)
                {
                    // Force focus to the header if not already focused.
                    if (!(FocusManager.GetFocusedElement(this) is Expander))
                        expander.Focus();

                    // Toggle expansion.
                    expander.IsExpanded = !expander.IsExpanded;
                    e.Handled = true;
                }
            }
        }

        // NEW: Add method to update unread counts in feed headers
        private void UpdateUnreadCounts()
        {
            // Go through each Expander in the NewsList
            foreach (var item in NewsList.Items)
            {
                if (item is Expander expander && expander.Header is StackPanel headerPanel)
                {
                    // Count how many articles are unread and how many are new
                    int unreadCount = 0;
                    int newUnreadCount = 0;

                    if (expander.Content is ListBox listBox)
                    {
                        // Collect article links for batch processing
                        List<string> articlesToMarkAsDisplayed = new List<string>();

                        foreach (var listItem in listBox.Items)
                        {
                            if (listItem is ListBoxItem lbi && lbi.Tag is FeedItem fi && !string.IsNullOrEmpty(fi.Link))
                            {
                                string link = fi.Link;

                                // Track for marking as displayed
                                articlesToMarkAsDisplayed.Add(link);

                                // Count unread articles (not opened)
                                if (!articleHistory.IsOpened(link))
                                {
                                    unreadCount++;
                                    // Check if this is a new article not seen before
                                    if (!articleHistory.HasBeenDisplayed(link))
                                    {
                                        newUnreadCount++;
                                    }
                                }
                            }
                        }

                        // Mark all as displayed in one batch
                        if (articlesToMarkAsDisplayed.Any())
                        {
                            foreach (string link in articlesToMarkAsDisplayed)
                            {
                                articleHistory.MarkAsDisplayed(link);
                            }
                            articleHistory.Save();
                        }
                    }

                    // Find the title row and update/add the unread counter
                    if (headerPanel.Children.Count > 0 && headerPanel.Children[0] is StackPanel titleRow)
                    {
                        // Get the title block which is always first
                        if (titleRow.Children.Count > 0 && titleRow.Children[0] is TextBlock titleBlock)
                        {
                            // Remove any existing counter block
                            if (titleRow.Children.Count > 1)
                            {
                                titleRow.Children.RemoveAt(1);
                            }

                            // Add unread count if there are any
                            if (unreadCount > 0)
                            {
                                TextBlock countBlock = new TextBlock
                                {
                                    Text = newUnreadCount > 0 ?
                                        $"({unreadCount} unread, +{newUnreadCount} new)" :
                                        $"({unreadCount} unread)",
                                    Foreground = newUnreadCount > 0 ? Brushes.LightGreen : Brushes.Cyan,
                                    FontWeight = FontWeights.Bold,
                                    FontSize = 16,
                                    VerticalAlignment = VerticalAlignment.Center,
                                    Margin = new Thickness(5, 0, 0, 0)
                                };
                                titleRow.Children.Add(countBlock);
                            }
                        }
                    }
                }
            }
        }

        // Add a new method to mark an article as read without opening it
        private void MarkArticleAsRead(FeedItem feedItem)
        {
            if (feedItem == null || string.IsNullOrEmpty(feedItem.Link))
            {
                Debug.WriteLine("Cannot mark as read: No link available.");
                return;
            }

            // Mark the article as opened in history
            articleHistory.MarkOpened(feedItem.Link);

            // Update directly in UI thread to ensure immediate visual update
            Dispatcher.Invoke(() =>
            {
                // Get the read brush resource for consistent color
                SolidColorBrush readItemBrush = (SolidColorBrush)FindResource("ReadItemBrush")
                    ?? new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)); // #CCCCCC

                // Find and update the specific article's TextBlock directly
                foreach (var expanderObj in NewsList.Items)
                {
                    if (expanderObj is Expander expander && expander.Content is ListBox listBox)
                    {
                        foreach (var itemObj in listBox.Items)
                        {
                            if (itemObj is ListBoxItem lbi && lbi.Tag is FeedItem fi && fi.Link == feedItem.Link)
                            {
                                if (lbi.Content is StackPanel panel)
                                {
                                    foreach (var child in panel.Children)
                                    {
                                        if (child is StackPanel innerPanel)
                                        {
                                            foreach (var innerChild in innerPanel.Children)
                                            {
                                                if (innerChild is TextBlock tb && tb.Text.Contains(". "))
                                                {
                                                    // Directly set the Foreground here and force a refresh
                                                    tb.Foreground = readItemBrush;
                                                    tb.InvalidateVisual();
                                                    break;
                                                }
                                            }
                                        }
                                        else if (child is TextBlock tb && tb.Text.Contains(". "))
                                        {
                                            // Directly set the Foreground here and force a refresh
                                            tb.Foreground = readItemBrush;
                                            tb.InvalidateVisual();
                                            break;
                                        }
                                    }
                                    // Force the panel to refresh
                                    panel.InvalidateVisual();
                                }
                                // Force the ListBoxItem to refresh
                                lbi.InvalidateVisual();
                            }
                        }
                        // Update the ListBox UI
                        listBox.UpdateLayout();
                        listBox.InvalidateVisual();
                    }
                }

                // Update unread counts in feed headers
                UpdateUnreadCounts();
            }, DispatcherPriority.Render);

            // Save updated history
            articleHistory.Save();

            Debug.WriteLine($"Article marked as read: {feedItem.Link} with UI directly updated");
        }

        // Add a new method to copy an article's link to the clipboard
        private void CopyArticleLink(FeedItem feedItem)
        {
            if (feedItem == null || string.IsNullOrEmpty(feedItem.Link))
            {
                Debug.WriteLine("Cannot copy link: No link available.");
                return;
            }

            try
            {
                // Copy the link to clipboard
                Clipboard.SetText(feedItem.Link);

                // Show a short status message (optional)
                Debug.WriteLine($"Copied link to clipboard: {feedItem.Link}");

                // You could display a temporary tooltip or status message here
                // if you want to give the user feedback that the link was copied
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to copy link: {ex.Message}");
                MessageBox.Show($"Failed to copy link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Add these methods for settings management

        private void ApplySettingsToArticle()
        {
            try
            {
                // Apply the saved fonts and sizes
                UpdateApplicationFonts();
                ArticleRichTextBox.FontSize = settings.ArticleFontSize;

                // Update article format if there's content
                if (ArticleRichTextBox.Document != null)
                {
                    UpdateArticleFormat();
                }

                Debug.WriteLine($"Applied settings: Font={settings.ArticleFontFamily}, Size={settings.ArticleFontSize}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error applying settings: {ex.Message}");
            }
        }

        private void HandleFormatShortcut(Key key, ModifierKeys modifiers)
        {
            bool changed = false;

            // Shift+Ctrl for paragraph side margin
            if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift &&
                (modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (key == Key.Add || key == Key.OemPlus)
                {
                    Debug.WriteLine("Shift + Ctrl + Plus detected, increasing paragraph side margin...");
                    settings.ParagraphSideMargin += 20;
                    changed = true;
                }
                else if (key == Key.Subtract || key == Key.OemMinus)
                {
                    Debug.WriteLine("Shift + Ctrl + Minus detected, decreasing paragraph side margin...");
                    settings.ParagraphSideMargin = Math.Max(20, settings.ParagraphSideMargin - 20);
                    changed = true;
                }
            }
            // Ctrl alone for font size
            else if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (key == Key.Add || key == Key.OemPlus)
                {
                    settings.ArticleFontSize += 1;
                    Debug.WriteLine($"Increased article font size to {settings.ArticleFontSize}");
                    changed = true;
                }
                else if (key == Key.Subtract || key == Key.OemMinus)
                {
                    settings.ArticleFontSize = Math.Max(10, settings.ArticleFontSize - 1);
                    Debug.WriteLine($"Decreased article font size to {settings.ArticleFontSize}");
                    changed = true;
                }
            }
            // Shift alone for paragraph bottom spacing
            else if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                if (key == Key.Add || key == Key.OemPlus)
                {
                    settings.ArticleMargin += 2;
                    Debug.WriteLine($"Increased article paragraph bottom margin to {settings.ArticleMargin}");
                    changed = true;
                }
                else if (key == Key.Subtract || key == Key.OemMinus)
                {
                    settings.ArticleMargin = Math.Max(2, settings.ArticleMargin - 2);
                    Debug.WriteLine($"Decreased article paragraph bottom margin to {settings.ArticleMargin}");
                    changed = true;
                }
            }

            if (changed)
            {
                // Reapply article formatting and save
                UpdateArticleFormat();
                settings.Save();
            }
        }

        // Add a button click handler to switch between Ubuntu Mono and Times New Roman
        private void SwitchFont_Click(object sender, RoutedEventArgs e)
        {
            string currentFont = settings.ArticleFontFamily;
            settings.ArticleFontFamily = (currentFont == "Ubuntu Mono") ? "Times New Roman" : "Ubuntu Mono";

            UpdateApplicationFonts();
            settings.Save();

            Debug.WriteLine($"Font switched to {settings.ArticleFontFamily}");
        }

        // Update fonts across the entire application
        private void UpdateApplicationFonts()
        {
            try
            {
                FontFamily fontFamily = new FontFamily(settings.ArticleFontFamily);
                ArticleRichTextBox.FontFamily = fontFamily;
                NewsList.FontFamily = fontFamily;

                // If an article is open, apply the new font to its paragraphs
                if (ArticleRichTextBox.Document != null)
                {
                    foreach (Block block in ArticleRichTextBox.Document.Blocks)
                    {
                        if (block is Paragraph paragraph)
                        {
                            new TextRange(paragraph.ContentStart, paragraph.ContentEnd)
                                .ApplyPropertyValue(TextElement.FontFamilyProperty, fontFamily);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating fonts: {ex.Message}");
            }
        }

        // NEW: When a header (Expander) is selected via arrow keys, focus it immediately.
        private void NewsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NewsList.SelectedItem is Expander expander)
            {
                expander.Focus();
            }
        }

        // NEW: Toggle button handler
        private void ToggleImages_Click(object sender, RoutedEventArgs e)
        {
            showArticleImages = !showArticleImages;
            Debug.WriteLine($"Article images toggled {(showArticleImages ? "ON" : "OFF")}");
            // Refresh the feed list to update image visibility
            // You can either reload feeds or update existing items; here we reload for simplicity.
            _ = LoadFeeds();
        }

        // NEW: Helper method to update the read style for a specific FeedItem in the UI
        private void UpdateReadStyleForFeedItem(FeedItem feedItem)
        {
            if (feedItem == null || string.IsNullOrEmpty(feedItem.Link))
                return;

            // Get the read brush resource
            SolidColorBrush readItemBrush = (SolidColorBrush)FindResource("ReadItemBrush")
                ?? new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)); // fallback darker gray

            // Iterate through each expander in NewsList
            foreach (var expanderObj in NewsList.Items)
            {
                if (expanderObj is Expander expander && expander.Content is ListBox listBox)
                {
                    foreach (var itemObj in listBox.Items)
                    {
                        if (itemObj is ListBoxItem lbi && lbi.Tag is FeedItem fi && fi.Link == feedItem.Link)
                        {
                            if (lbi.Content is StackPanel panel)
                            {
                                foreach (var child in panel.Children)
                                {
                                    if (child is TextBlock tb && tb.Text.Contains(". "))
                                    {
                                        tb.Foreground = readItemBrush;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // Add missing helper method for visual tree upward search
        private T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null)
                return null;
            if (parentObject is T parent)
                return parent;
            return FindVisualParent<T>(parentObject);
        }

        /// <summary>
        /// Appends text to the status bar with an optional newline
        /// </summary>
        /// <param name="message">The message to append</param>
        /// <param name="addNewLine">Whether to add a new line before the message (default: true)</param>
        private void AppendStatus(string message, bool addNewLine = true)
        {
            if (StatusText == null || StatusScrollViewer == null) return;

            // If status is empty, don't add a newline first
            if (string.IsNullOrEmpty(StatusText.Text))
            {
                StatusText.Text = message;
            }
            else
            {
                // Otherwise append with or without a newline
                StatusText.Text = addNewLine
                    ? $"{StatusText.Text}\n{message}"
                    : $"{StatusText.Text} {message}";
            }

            // Scroll to the bottom
            StatusScrollViewer.ScrollToEnd();

            // For better visibility in the debug console
            Debug.WriteLine($"Status: {message}");
        }

        // Toggle dark mode
        private void ToggleDarkMode_Click(object sender, RoutedEventArgs e)
        {
            _isDarkMode = !_isDarkMode;

            // Save the dark mode setting to application settings
            settings.IsDarkMode = _isDarkMode;
            settings.Save();

            ApplyTheme();
        }

        // Apply the current theme (light or dark)
        private void ApplyTheme()
        {
            // Update the global dark mode setting in the article parser
            ArticleParser.SetDarkMode(_isDarkMode);

            if (_isDarkMode)
            {
                // Apply dark mode
                ArticleRichTextBox.Background = (SolidColorBrush)Application.Current.Resources["DarkModeBackground"];

                // Apply dark background and beige text to FlowDocument
                if (ArticleRichTextBox.Document != null)
                {
                    ArticleRichTextBox.Document.Background = (SolidColorBrush)Application.Current.Resources["DarkModeBackground"];

                    // Update text color for all paragraphs
                    foreach (Block block in ArticleRichTextBox.Document.Blocks)
                    {
                        if (block is Paragraph para)
                        {
                            // If this is a summary paragraph (has background color and padding)
                            if (para.Padding.Top > 0 && para.Background != null)
                            {
                                para.Background = new SolidColorBrush(Color.FromRgb(0, 0, 0)); // Black background in dark mode
                                para.Foreground = (SolidColorBrush)Application.Current.Resources["DarkModeText"];
                                para.BorderBrush = Brushes.DarkGray;
                            }
                            else
                            {
                                // Regular paragraph
                                para.Foreground = (SolidColorBrush)Application.Current.Resources["DarkModeText"];
                            }
                        }
                        else if (block is Table table)
                        {
                            // Update all table cells to use dark mode colors
                            UpdateTableForDarkMode(table, true);
                        }
                    }
                }
            }
            else
            {
                // Apply light mode with paper texture
                // First ensure ArticleBackground is properly set
                if (ArticleBackground.ImageSource == null)
                {
                    LoadArticleBackground();
                }

                // Set the RichTextBox background to use the background image
                ArticleRichTextBox.Background = ArticleBackground;

                // Update text color for paragraphs
                if (ArticleRichTextBox.Document != null)
                {
                    ArticleRichTextBox.Document.Background = Brushes.Transparent;

                    foreach (Block block in ArticleRichTextBox.Document.Blocks)
                    {
                        if (block is Paragraph para)
                        {
                            // If this is a summary paragraph (has background color and padding)
                            if (para.Padding.Top > 0 && para.Background != null)
                            {
                                para.Background = new SolidColorBrush(Color.FromRgb(245, 245, 220)); // Beige background in light mode
                                para.Foreground = Brushes.Black;
                                para.BorderBrush = Brushes.Gray;
                            }
                            else
                            {
                                // Regular paragraph
                                para.Foreground = Brushes.Black;
                            }
                        }
                        else if (block is Table table)
                        {
                            // Update all table cells to use light mode colors
                            UpdateTableForDarkMode(table, false);
                        }
                    }
                }
            }
        }

        // Helper method to update table colors based on dark mode setting
        private void UpdateTableForDarkMode(Table table, bool isDarkMode)
        {
            Brush textColor = isDarkMode
                ? (SolidColorBrush)Application.Current.Resources["DarkModeText"]
                : Brushes.Black;

            // Get header background color
            Brush headerBackground = isDarkMode ? Brushes.DimGray : Brushes.LightGray;

            // Process all row groups in the table
            foreach (TableRowGroup rowGroup in table.RowGroups)
            {
                foreach (TableRow row in rowGroup.Rows)
                {
                    // Check if this is a header row (typically has background color)
                    bool isHeaderRow = row.Background != null;

                    if (isHeaderRow)
                    {
                        // Update header row background
                        row.Background = headerBackground;
                    }

                    // Update all cells in this row
                    foreach (TableCell cell in row.Cells)
                    {
                        foreach (Block cellBlock in cell.Blocks)
                        {
                            if (cellBlock is Paragraph cellPara)
                            {
                                // Use white text for header cells in dark mode, black for light mode
                                if (isHeaderRow)
                                {
                                    cellPara.Foreground = isDarkMode ? Brushes.White : Brushes.Black;
                                }
                                else
                                {
                                    // For regular rows, first check if there are any inline elements
                                    if (cellPara.Inlines.Count > 0)
                                    {
                                        // Check each inline element for special coloring
                                        foreach (Inline inline in cellPara.Inlines)
                                        {
                                            if (inline is Run run)
                                            {
                                                // Skip if the text has special coloring for +/- values
                                                if (run.Text.StartsWith("+") && run.Foreground == Brushes.Green)
                                                    continue;
                                                if (run.Text.StartsWith("-") && run.Foreground == Brushes.Red)
                                                    continue;

                                                // Update the color for regular text
                                                run.Foreground = textColor;
                                            }
                                            // Handle Bold elements which might contain player names
                                            else if (inline is Bold bold)
                                            {
                                                foreach (Inline boldInline in bold.Inlines)
                                                {
                                                    if (boldInline is Run boldRun)
                                                    {
                                                        boldRun.Foreground = textColor;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    // If no inlines, update the paragraph foreground
                                    else
                                    {
                                        cellPara.Foreground = textColor;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
