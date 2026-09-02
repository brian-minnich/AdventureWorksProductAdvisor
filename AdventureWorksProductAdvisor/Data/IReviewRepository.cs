using System.Collections.Generic;
using System.Threading.Tasks;

namespace AdventureWorksProductAdvisor.Data
{
    // The only thing the rest of the app knows about how reviews are
    // stored. ReviewRepository is the real (ADO.NET/SQL Server)
    // implementation; tests substitute a Moq mock of this interface instead,
    // so CSharpAskServiceTests etc. never touch a real database.
    public interface IReviewRepository
    {
        Task<List<ReviewRecord>> GetAllReviewsAsync();
        Task SaveEmbeddingAsync(int reviewId, float[] embedding);
    }
}
