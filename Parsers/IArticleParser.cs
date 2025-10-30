using System.Threading.Tasks;
using System.Windows.Documents;
using Newz.Settings;

namespace Newz.Parsers
{
    public interface IArticleParser
    {
        Task<FlowDocument> ParseArticle(string html, FeedItem feedData, AppSettings settings, double windowWidth);
    }
}