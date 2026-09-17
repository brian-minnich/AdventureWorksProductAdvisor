using System.Collections.Generic;
using System.Threading.Tasks;
using AdventureWorksProductAdvisor.Data;
using AdventureWorksProductAdvisor.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace AdventureWorksProductAdvisor.Tests.Services
{
    // Tests the pipeline orchestration in CSharpAskService with all three of
    // its dependencies (IReviewRepository, IEmbeddingService,
    // IChatCompletionService) mocked - so this test is about "does it call
    // the right things with the right data," not about real embeddings or a
    // real chat model.
    [TestClass]
    public class CSharpAskServiceTests
    {
        [TestMethod]
        public async Task AskAsync_RetrievesTopNReviewsAndReturnsAnswer()
        {
            // Two reviews with embeddings that point in opposite directions
            // ({1,0} vs {0,1}) - since the "question" embedding below is
            // also {1,0}, the Mountain Bike review should be the closest
            // match and the Road Bike review should not.
            var reviews = new List<ReviewRecord>
            {
                new ReviewRecord { ReviewId = 1, ProductName = "Mountain Bike", Rating = 5, ReviewTitle = "Great", ReviewText = "Loved it", Embedding = new float[] { 1f, 0f } },
                new ReviewRecord { ReviewId = 2, ProductName = "Road Bike", Rating = 2, ReviewTitle = "Meh", ReviewText = "It was okay", Embedding = new float[] { 0f, 1f } }
            };

            var reviewRepository = new Mock<IReviewRepository>();
            reviewRepository.Setup(r => r.GetAllReviewsAsync()).ReturnsAsync(reviews);

            var embeddingService = new Mock<IEmbeddingService>();
            embeddingService.Setup(e => e.GetEmbeddingAsync("What bike is best for trails?"))
                .ReturnsAsync(new float[] { 1f, 0f });

            var chatCompletionService = new Mock<IChatCompletionService>();
            // This predicate checks the prompt built for the chat model
            // contains the Mountain Bike review and the question, AND does
            // NOT contain the Road Bike review - with topN=1 below, only the
            // top match should make it into the prompt. If topN truncation
            // were broken and both reviews leaked in, this Setup wouldn't
            // match the call, GetCompletionAsync would return null (Moq's
            // default for an unmatched call), and the Assert.AreEqual below
            // would fail - so this test genuinely depends on topN working.
            chatCompletionService
                .Setup(c => c.GetCompletionAsync(
                    It.Is<string>(p => p.Contains("Mountain Bike") && p.Contains("What bike is best for trails?") && !p.Contains("Road Bike")),
                    500))
                .ReturnsAsync("The Mountain Bike is best for trails.");

            var service = new CSharpAskService(reviewRepository.Object, embeddingService.Object, chatCompletionService.Object);

            var result = await service.AskAsync("What bike is best for trails?", 500, 1);

            Assert.IsTrue(result.Success);
            Assert.AreEqual("The Mountain Bike is best for trails.", result.Answer);
            chatCompletionService.Verify(c => c.GetCompletionAsync(It.IsAny<string>(), 500), Times.Once);
        }

        [TestMethod]
        public async Task AskAsync_NoMatchingReviews_ReturnsFriendlyAnswerWithoutCallingChatCompletion()
        {
            // Empty review list - nothing can possibly match, so this
            // exercises the "no reviews found" short-circuit in AskAsync.
            var reviewRepository = new Mock<IReviewRepository>();
            reviewRepository.Setup(r => r.GetAllReviewsAsync()).ReturnsAsync(new List<ReviewRecord>());

            var embeddingService = new Mock<IEmbeddingService>();
            embeddingService.Setup(e => e.GetEmbeddingAsync(It.IsAny<string>())).ReturnsAsync(new float[] { 1f, 0f });

            // No Setup calls on this mock at all - if CSharpAskService called
            // GetCompletionAsync anyway, Moq would return its default (null),
            // and the Verify below would fail since it explicitly checks
            // the call never happened.
            var chatCompletionService = new Mock<IChatCompletionService>();

            var service = new CSharpAskService(reviewRepository.Object, embeddingService.Object, chatCompletionService.Object);

            var result = await service.AskAsync("Anything?", 500, 5);

            Assert.IsTrue(result.Success);
            Assert.AreEqual("No reviews found matching your query. Please try a different question.", result.Answer);
            // The point of this test: confirms the chat model is never
            // called (and therefore never billed for) when there's nothing
            // useful to answer from.
            chatCompletionService.Verify(c => c.GetCompletionAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }

        [TestMethod]
        public async Task AskAsync_BestMatchBelowSimilarityThreshold_ReturnsOffTopicAnswerWithoutCallingChatCompletion()
        {
            // The question's embedding ({0,1}) is orthogonal to every
            // review's embedding ({1,0}) - cosine similarity 0, same as
            // asking a bike-product-review set about cars. With the 0.5
            // threshold passed to the constructor below, that's well under
            // the bar even though a review WAS found (unlike the
            // no-matching-reviews test above, which has an empty review
            // list) - this is what actually exercises the threshold check,
            // not the emptiness check.
            var reviews = new List<ReviewRecord>
            {
                new ReviewRecord { ReviewId = 1, ProductName = "Mountain Bike", Rating = 5, ReviewTitle = "Great", ReviewText = "Loved it", Embedding = new float[] { 1f, 0f } }
            };

            var reviewRepository = new Mock<IReviewRepository>();
            reviewRepository.Setup(r => r.GetAllReviewsAsync()).ReturnsAsync(reviews);

            var embeddingService = new Mock<IEmbeddingService>();
            embeddingService.Setup(e => e.GetEmbeddingAsync("What's the best car for a road trip?"))
                .ReturnsAsync(new float[] { 0f, 1f });

            // No Setup calls on this mock at all - same reasoning as the
            // no-matching-reviews test: if CSharpAskService called
            // GetCompletionAsync anyway, the Verify below would catch it.
            var chatCompletionService = new Mock<IChatCompletionService>();

            var service = new CSharpAskService(reviewRepository.Object, embeddingService.Object, chatCompletionService.Object, minSimilarityScore: 0.5);

            var result = await service.AskAsync("What's the best car for a road trip?", 500, 5);

            Assert.IsTrue(result.Success);
            Assert.AreEqual("I can only answer questions about our products based on customer reviews. Please ask a product-related question.", result.Answer);
            chatCompletionService.Verify(c => c.GetCompletionAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }
    }
}
