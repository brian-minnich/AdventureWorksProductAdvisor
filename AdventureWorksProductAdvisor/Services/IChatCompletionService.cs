using System.Threading.Tasks;

namespace AdventureWorksProductAdvisor.Services
{
    public interface IChatCompletionService
    {
        Task<string> GetCompletionAsync(string prompt, int maxCompletionTokens);
    }
}
