using System.Collections.Generic;
using System.Threading.Tasks;

namespace AdventureWorksProductAdvisor.Data
{
    public interface IReviewRepository
    {
        Task<List<ReviewRecord>> GetAllReviewsAsync();
        Task SaveEmbeddingAsync(int reviewId, float[] embedding);
    }
}
