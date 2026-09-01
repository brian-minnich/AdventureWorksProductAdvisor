using System.Threading.Tasks;

namespace AdventureWorksProductAdvisor.Services
{
    public interface IAskService
    {
        Task<AskServiceResult> AskAsync(string question, int maxCompletionTokens, int topN);
    }
}
