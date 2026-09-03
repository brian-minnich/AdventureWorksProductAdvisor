using System.Threading.Tasks;

namespace AdventureWorksProductAdvisor.Services
{
    // Deliberately provider-agnostic: takes plain text in, returns a plain
    // float[] out. AzureOpenAiEmbeddingService is the only class that knows
    // this is backed by Azure OpenAI specifically - nothing Azure-shaped
    // (URLs, JSON payloads, credentials) appears in this interface, so a
    // different provider could implement it later without touching any caller.
    public interface IEmbeddingService
    {
        Task<float[]> GetEmbeddingAsync(string text);
    }
}
