using System.Collections.Generic;
using System.Threading.Tasks;
using AdventureWorksProductAdvisor.Data;
using AdventureWorksProductAdvisor.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace AdventureWorksProductAdvisor.Tests.Services
{
    [TestClass]
    public class CSharpAskServiceTests
    {
        [TestMethod]
        public async Task AskAsync_RetrievesTopNReviewsAndReturnsAnswer()
        {
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
            var reviewRepository = new Mock<IReviewRepository>();
            reviewRepository.Setup(r => r.GetAllReviewsAsync()).ReturnsAsync(new List<ReviewRecord>());

            var embeddingService = new Mock<IEmbeddingService>();
            embeddingService.Setup(e => e.GetEmbeddingAsync(It.IsAny<string>())).ReturnsAsync(new float[] { 1f, 0f });

            var chatCompletionService = new Mock<IChatCompletionService>();

            var service = new CSharpAskService(reviewRepository.Object, embeddingService.Object, chatCompletionService.Object);

            var result = await service.AskAsync("Anything?", 500, 5);

            Assert.IsTrue(result.Success);
            Assert.AreEqual("No reviews found matching your query. Please try a different question.", result.Answer);
            chatCompletionService.Verify(c => c.GetCompletionAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }
    }
}
