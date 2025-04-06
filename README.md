# Nius - News Aggregator

Nius is a lightweight WPF-based news aggregator application that fetches and displays RSS feeds. It supports features like image previews, article history, and customizable settings.

## Features
- **RSS Feed Aggregation**: Fetch and display RSS feeds with image previews.
- **Article Management**: Mark articles as read/unread; view article history across sessions.
- **Dark Mode**: 
  - Toggle between light mode (paper texture with black text) and dark mode (dark gray background with bright beige text).
  - The dark mode theme updates all article elements including summary blocks, tables, and player stats seamlessly.
- **AI Article Summary**:
  - Automatically generate concise AI-based summaries for articles using Google’s Gemini API.
  - Display metadata such as processing time and word count reductions alongside the summary.
- **Performance Optimization**:
  - Asynchronous operations ensure smooth feed loading and UI rendering even with a large number of articles.

## Requirements
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Windows OS (WPF-based application)

## Installation
1. Clone the repository:
    ```bash
    git clone https://github.com/tmthyDXTR/nius.git
    cd nius
    ```

2. Build the project:
    ```bash
    dotnet build
    ```

3. Run the application:
    ```bash
    dotnet run
    ```

## Usage
- Add RSS feed URLs in the settings.
- Expand headers to view articles.
- Double-click an article to open it in your browser.

## Preview Images
<img src="img/2025-04-06 19-57-04.png" alt="Preview 1" width="200" />
<img src="img/2025-04-06 19-54-42.png" alt="Preview 2" width="200" />
<img src="img/2025-04-06 19-55-09.png" alt="Preview 3" width="200" />

## Implementation Targets
Here are the planned features and improvements for Nius:

1. **Crossplatform Port with .NET MAUI for Android**:
   - Port the application to .NET MAUI for Android.
2. **Filters**:
    - Add filters to sort and display articles based on:
        - Keywords
        - Publication date
        - Read/unread status
    - Allow users to save custom filter configurations.

3. **UI for Managing Feeds**:
    - Add a user-friendly interface for:
        - Adding new RSS feed URLs.
        - Removing unwanted feeds.
        - Sorting feeds alphabetically or by custom order.

4. **Improved Article History**:
    - Track read/unread articles across sessions.
    - Provide an option to clear or archive old articles.

5. **Performance Optimization**:
    - Optimize feed loading and UI rendering for large numbers of articles.
    - Asynchronous operations for smoother user experience.

## Contributing
Contributions are welcome! Feel free to open issues or submit pull requests.

