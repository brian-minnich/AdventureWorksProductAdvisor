using System.Threading.Tasks;

namespace AdventureWorksProductAdvisor.Services
{
    // The shared contract both RAG pipelines implement. AskController only
    // ever talks to this interface (via the dictionary AskServiceFactory
    // builds) - it has no idea whether it's calling CSharpAskService or,
    // later, a stored-procedure-backed implementation. That's what makes
    // adding a second pipeline a matter of writing a new class and adding
    // one dictionary entry, not changing the controller.
    public interface IAskService
    {
        Task<AskServiceResult> AskAsync(string question, int maxCompletionTokens, int topN);
    }
}
