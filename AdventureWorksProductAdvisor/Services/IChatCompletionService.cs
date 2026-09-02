using System.Threading.Tasks;

namespace AdventureWorksProductAdvisor.Services
{
    // Same idea as IEmbeddingService: a plain string prompt in, a plain
    // string answer out. AzureOpenAiChatCompletionService is the only class
    // that knows this call goes to Azure OpenAI's chat completions endpoint.
    public interface IChatCompletionService
    {
        Task<string> GetCompletionAsync(string prompt, int maxCompletionTokens);
    }
}
