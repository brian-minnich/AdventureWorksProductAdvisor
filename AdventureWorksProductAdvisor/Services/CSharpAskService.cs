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

        // Below this cosine similarity, the best-matching review isn't
        // actually about what was asked - see AskAsync's threshold check
        // below. 0.35 was picked from real scores against this dataset's
        // embeddings (text-embedding-3-small): clearly-unrelated questions
        // ("What is the capital of France?", "How do I reset my email
        // password?") scored ~0.13, and even an off-topic question that
        // mimics a real product-recommendation question's phrasing ("What
        // is the best car for a road trip?") only scored ~0.34, while
        // genuine on-topic questions scored 0.47+. Still worth re-checking
        // if the embedding deployment or review data changes meaningfully,
        // since this is calibrated against a handful of examples, not a
        // large sample.
        private readonly double _minSimilarityScore;

        // SimilarityRanker is pure math with no dependencies of its own, so
        // unlike the three above it's just created directly here rather than
        // injected - there's nothing to fake, since its behavior is fully
        // deterministic given its inputs.
        private readonly SimilarityRanker _ranker = new SimilarityRanker();

        public CSharpAskService(
            IReviewRepository reviewRepository,
            IEmbeddingService embeddingService,
            IChatCompletionService chatCompletionService,
            double minSimilarityScore = 0.35)
        {
            _reviewRepository = reviewRepository;
            _embeddingService = embeddingService;
            _chatCompletionService = chatCompletionService;
            _minSimilarityScore = minSimilarityScore;
        }

        public async Task<AskServiceResult> AskAsync(string question, int maxCompletionTokens, int topN)
        {
            // Steps 1 and 2 don't depend on each other's result (embedding the
            // question and loading the reviews from the database are
            // independent), so they run concurrently via Task.WhenAll instead
            // of one after another - this call takes roughly as long as the
            // slower of the two, not the sum of both.
            var embeddingTask = _embeddingService.GetEmbeddingAsync(question);
            var reviewsTask = _reviewRepository.GetAllReviewsAsync();
            await Task.WhenAll(embeddingTask, reviewsTask);
            var queryEmbedding = embeddingTask.Result;
            var allReviews = reviewsTask.Result;

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

            // Even the best match isn't actually close to the question (e.g.
            // "what's the best car" against a bike-product review set) - stop
            // here too, rather than sending clearly-off-topic questions to
            // the chat model with irrelevant reviews as "context." Checking
            // topReviews[0].Score (the highest of the bunch, since GetTopN
            // orders descending) is a much cheaper guard than a dedicated
            // classification call, and reuses a score this pipeline was
            // already computing for ranking.
            if (topReviews[0].Score < _minSimilarityScore)
            {
                return new AskServiceResult
                {
                    Success = true,
                    Answer = "I can only answer questions about our products based on customer reviews. Please ask a product-related question."
                };
            }

            // Step 4: build a prompt containing the retrieved reviews plus
            // the original question, then ask the chat model to answer using
            // only that context.
            var prompt = BuildPrompt(topReviews.Select(r => r.Review).ToList(), question);
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
