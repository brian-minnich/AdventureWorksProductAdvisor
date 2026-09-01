using System.Threading.Tasks;
using AdventureWorksProductAdvisor.Data;

namespace AdventureWorksProductAdvisor.Services
{
    public class ReviewEmbeddingBackfillRunner
    {
        private readonly IReviewRepository _reviewRepository;
        private readonly IEmbeddingService _embeddingService;

        public ReviewEmbeddingBackfillRunner(IReviewRepository reviewRepository, IEmbeddingService embeddingService)
        {
            _reviewRepository = reviewRepository;
            _embeddingService = embeddingService;
        }

        public async Task<int> RunAsync()
        {
            var reviews = await _reviewRepository.GetAllReviewsAsync();
            var count = 0;
            foreach (var review in reviews)
            {
                var embedding = await _embeddingService.GetEmbeddingAsync(review.ReviewText);
                await _reviewRepository.SaveEmbeddingAsync(review.ReviewId, embedding);
                count++;
            }
            return count;
        }
    }
}
