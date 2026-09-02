using System.Linq;
using System.Threading.Tasks;
using AdventureWorksProductAdvisor.Data;

namespace AdventureWorksProductAdvisor.Services
{
    // The one-time (well, safely re-runnable) job that gives every review an
    // embedding, so SimilarityRanker has something to compare questions
    // against. Triggered manually via AdminController - there's no
    // scheduled job or automatic trigger for this in the app.
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
            // Only embed reviews that don't already have one - this is what
            // makes it safe to call this endpoint more than once (e.g. after
            // new reviews were added, or after a partial failure): already-
            // embedded reviews are skipped rather than re-paying for a new
            // embedding call.
            var reviewsToEmbed = reviews.Where(r => r.Embedding == null).ToList();
            foreach (var review in reviewsToEmbed)
            {
                // Combine product name + title + body into one string before
                // embedding, so the vector captures the product being
                // discussed, not just the raw review text - otherwise a
                // question like "what do people say about the mountain
                // bike?" would have no product-name signal to match against.
                var textToEmbed = $"{review.ProductName}. {review.ReviewTitle}. {review.ReviewText}";
                var embedding = await _embeddingService.GetEmbeddingAsync(textToEmbed);
                await _reviewRepository.SaveEmbeddingAsync(review.ReviewId, embedding);
            }
            return reviewsToEmbed.Count;
        }
    }
}
