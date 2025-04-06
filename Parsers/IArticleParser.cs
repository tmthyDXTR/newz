using System.Threading.Tasks;
using System.Windows.Documents;
using Nius.Settings;

namespace Nius.Parsers
{
    public interface IArticleParser
    {
        Task<FlowDocument> ParseArticle(string html, FeedItem feedData, AppSettings settings, double windowWidth);
    }
}