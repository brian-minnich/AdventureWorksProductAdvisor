using System.Linq;
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
            var reviewsToEmbed = reviews.Where(r => r.Embedding == null).ToList();
            foreach (var review in reviewsToEmbed)
            {
                var textToEmbed = $"{review.ProductName}. {review.ReviewTitle}. {review.ReviewText}";
                var embedding = await _embeddingService.GetEmbeddingAsync(textToEmbed);
                await _reviewRepository.SaveEmbeddingAsync(review.ReviewId, embedding);
            }
            return reviewsToEmbed.Count;
        }
    }
}
