using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AdventureWorksProductAdvisor.Data;
using AdventureWorksProductAdvisor.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace AdventureWorksProductAdvisor.Tests.Services
{
    [TestClass]
    public class ReviewEmbeddingBackfillRunnerTests
    {
        [TestMethod]
        public async Task RunAsync_EmbedsAndSavesEveryReview()
        {
            var reviews = new List<ReviewRecord>
            {
                new ReviewRecord { ReviewId = 1, ProductName = "Mountain Bike", ReviewTitle = "Great", ReviewText = "Loved it" },
                new ReviewRecord { ReviewId = 2, ProductName = "Road Bike", ReviewTitle = "Meh", ReviewText = "It was okay" }
            };

            var reviewRepository = new Mock<IReviewRepository>();
            reviewRepository.Setup(r => r.GetAllReviewsAsync()).ReturnsAsync(reviews);

            var embeddingService = new Mock<IEmbeddingService>();
            embeddingService.Setup(e => e.GetEmbeddingAsync("Mountain Bike. Great. Loved it")).ReturnsAsync(new float[] { 0.5f });
            embeddingService.Setup(e => e.GetEmbeddingAsync("Road Bike. Meh. It was okay")).ReturnsAsync(new float[] { 0.25f });

            var runner = new ReviewEmbeddingBackfillRunner(reviewRepository.Object, embeddingService.Object);

            var count = await runner.RunAsync();

            Assert.AreEqual(2, count);
            reviewRepository.Verify(
                r => r.SaveEmbeddingAsync(1, It.Is<float[]>(a => a.SequenceEqual(new float[] { 0.5f }))), Times.Once);
            reviewRepository.Verify(
                r => r.SaveEmbeddingAsync(2, It.Is<float[]>(a => a.SequenceEqual(new float[] { 0.25f }))), Times.Once);
        }
    }
}
