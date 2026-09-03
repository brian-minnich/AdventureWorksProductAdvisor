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
            // Neither review sets Embedding, so both default to null -
            // meaning both are eligible to be embedded by RunAsync's
            // `Where(r => r.Embedding == null)` filter.
            var reviews = new List<ReviewRecord>
            {
                new ReviewRecord { ReviewId = 1, ProductName = "Mountain Bike", ReviewTitle = "Great", ReviewText = "Loved it" },
                new ReviewRecord { ReviewId = 2, ProductName = "Road Bike", ReviewTitle = "Meh", ReviewText = "It was okay" }
            };

            var reviewRepository = new Mock<IReviewRepository>();
            reviewRepository.Setup(r => r.GetAllReviewsAsync()).ReturnsAsync(reviews);

            var embeddingService = new Mock<IEmbeddingService>();
            // These Setup strings must match exactly what
            // ReviewEmbeddingBackfillRunner.RunAsync builds
            // ("{ProductName}. {ReviewTitle}. {ReviewText}") - if that
            // combination logic ever changes, these two lines need updating too.
            embeddingService.Setup(e => e.GetEmbeddingAsync("Mountain Bike. Great. Loved it")).ReturnsAsync(new float[] { 0.5f });
            embeddingService.Setup(e => e.GetEmbeddingAsync("Road Bike. Meh. It was okay")).ReturnsAsync(new float[] { 0.25f });

            var runner = new ReviewEmbeddingBackfillRunner(reviewRepository.Object, embeddingService.Object);

            var count = await runner.RunAsync();

            Assert.AreEqual(2, count);
            // Moq compares arrays by reference by default, so a plain
            // It.Is<float[]> equality check wouldn't work here -
            // SequenceEqual is used to compare the array's actual contents instead.
            reviewRepository.Verify(
                r => r.SaveEmbeddingAsync(1, It.Is<float[]>(a => a.SequenceEqual(new float[] { 0.5f }))), Times.Once);
            reviewRepository.Verify(
                r => r.SaveEmbeddingAsync(2, It.Is<float[]>(a => a.SequenceEqual(new float[] { 0.25f }))), Times.Once);
        }
    }
}
