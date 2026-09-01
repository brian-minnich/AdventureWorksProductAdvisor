using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AdventureWorksProductAdvisor.Data;

namespace AdventureWorksProductAdvisor.Services
{
    public class CSharpAskService : IAskService
    {
        private readonly IReviewRepository _reviewRepository;
        private readonly IEmbeddingService _embeddingService;
        private readonly IChatCompletionService _chatCompletionService;
        private readonly SimilarityRanker _ranker = new SimilarityRanker();

        public CSharpAskService(
            IReviewRepository reviewRepository, IEmbeddingService embeddingService, IChatCompletionService chatCompletionService)
        {
            _reviewRepository = reviewRepository;
            _embeddingService = embeddingService;
            _chatCompletionService = chatCompletionService;
        }

        public async Task<AskServiceResult> AskAsync(string question, int maxCompletionTokens, int topN)
        {
            var queryEmbedding = await _embeddingService.GetEmbeddingAsync(question);
            var allReviews = await _reviewRepository.GetAllReviewsAsync();
            var topReviews = _ranker.GetTopN(allReviews, queryEmbedding, topN);

            if (topReviews.Count == 0)
            {
                return new AskServiceResult
                {
                    Success = true,
                    Answer = "No reviews found matching your query. Please try a different question."
                };
            }

            var prompt = BuildPrompt(topReviews, question);
            var answer = await _chatCompletionService.GetCompletionAsync(prompt, maxCompletionTokens);

            return new AskServiceResult { Success = true, Answer = answer };
        }

        private static string BuildPrompt(System.Collections.Generic.List<ReviewRecord> reviews, string question)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Product reviews:");
            foreach (var r in reviews)
            {
                sb.AppendLine($"- Product: {r.ProductName}, Rating: {r.Rating}/5, Title: \"{r.ReviewTitle}\"");
                sb.AppendLine($"  {r.ReviewText}");
            }
            sb.AppendLine();
            sb.Append("Customer question: ").Append(question);
            return sb.ToString();
        }
    }
}
