using System.Threading.Tasks;

namespace AdventureWorksProductAdvisor.Services
{
    public interface IEmbeddingService
    {
        Task<float[]> GetEmbeddingAsync(string text);
    }
}
