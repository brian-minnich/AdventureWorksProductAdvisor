using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;
using AdventureWorksProductAdvisor.Controllers.Api;
using AdventureWorksProductAdvisor.Data;
using AdventureWorksProductAdvisor.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace AdventureWorksProductAdvisor.Tests.Controllers.Api
{
    [TestClass]
    public class AdminControllerTests
    {
        [TestMethod]
        public async Task BackfillEmbeddings_ReturnsCountOfEmbeddedReviews()
        {
            var reviews = new List<ReviewRecord> { new ReviewRecord { ReviewId = 1, ReviewText = "Loved it" } };
            var reviewRepository = new Mock<IReviewRepository>();
            reviewRepository.Setup(r => r.GetAllReviewsAsync()).ReturnsAsync(reviews);

            var embeddingService = new Mock<IEmbeddingService>();
            embeddingService.Setup(e => e.GetEmbeddingAsync(It.IsAny<string>())).ReturnsAsync(new float[] { 0.5f });

            // Rather than mocking ReviewEmbeddingBackfillRunner directly
            // (it's a concrete class, not an interface), this builds a real
            // one from mocked IReviewRepository/IEmbeddingService - so the
            // runner's own logic (already covered by
            // ReviewEmbeddingBackfillRunnerTests) also runs for real here.
            // This test's actual focus is just: does the controller call
            // RunAsync and return its count correctly?
            var runner = new ReviewEmbeddingBackfillRunner(reviewRepository.Object, embeddingService.Object);
            var controller = new AdminController(runner);
            controller.Configuration = new HttpConfiguration();
            controller.Request = new HttpRequestMessage();

            var result = await controller.BackfillEmbeddings();

            var okResult = result as OkNegotiatedContentResult<int>;
            Assert.IsNotNull(okResult);
            Assert.AreEqual(1, okResult.Content);
        }
    }
}
