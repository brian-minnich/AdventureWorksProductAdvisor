using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AdventureWorksProductAdvisor.Data;

namespace AdventureWorksProductAdvisor.Services
{
    // This class is the whole "RAG" (Retrieval-Augmented Generation) pipeline
    // for the C# side: turn the question into a vector, find the most
    // similar reviews, hand both to the chat model, return its answer.
    // It only talks to its three collaborators through interfaces
    // (IReviewRepository, IEmbeddingService, IChatCompletionService), which
    // is what makes CSharpAskServiceTests able to test this orchestration
    // logic with fake/mocked versions of all three, with no real database
    // or network call involved.
    public class CSharpAskService : IAskService
    {
        private readonly IReviewRepository _reviewRepository;
        private readonly IEmbeddingService _embeddingService;
        private readonly IChatCompletionService _chatCompletionService;

        // SimilarityRanker is pure math with no dependencies of its own, so
        // unlike the three above it's just created directly here rather than
        // injected - there's nothing to fake, since its behavior is fully
        // deterministic given its inputs.
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
            // Step 1: turn the question into the same kind of vector the
            // reviews were embedded into (see ReviewEmbeddingBackfillRunner),
            // so they can be compared.
            var queryEmbedding = await _embeddingService.GetEmbeddingAsync(question);

            // Step 2: load every review (each one already carries its own
            // pre-computed embedding, read from the ReviewEmbeddingJson column).
            var allReviews = await _reviewRepository.GetAllReviewsAsync();

            // Step 3: pick the topN reviews whose embeddings are closest to
            // the question's embedding.
            var topReviews = _ranker.GetTopN(allReviews, queryEmbedding, topN);

            // If nothing matched closely enough to even be worth showing,
            // stop here rather than calling the chat model with an empty/
            // useless context - this is a real cost guard, not just UX
            // polish, since it avoids paying for a chat completion call
            // that has nothing useful to answer from.
            if (topReviews.Count == 0)
            {
                return new AskServiceResult
                {
                    Success = true,
                    Answer = "No reviews found matching your query. Please try a different question."
                };
            }

            // Step 4: build a prompt containing the retrieved reviews plus
            // the original question, then ask the chat model to answer using
            // only that context.
            var prompt = BuildPrompt(topReviews, question);
            var answer = await _chatCompletionService.GetCompletionAsync(prompt, maxCompletionTokens);

            return new AskServiceResult { Success = true, Answer = answer };
        }

        // Turns the retrieved reviews into the block of text the chat model
        // actually reads. This is the "augmented" part of "Retrieval-
        // Augmented Generation" - the model never sees the whole review
        // table, only these few relevant excerpts.
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
