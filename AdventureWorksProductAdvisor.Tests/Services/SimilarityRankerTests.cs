using System.Collections.Generic;
using System.Linq;
using AdventureWorksProductAdvisor.Data;
using AdventureWorksProductAdvisor.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AdventureWorksProductAdvisor.Tests.Services
{
    [TestClass]
    public class SimilarityRankerTests
    {
        [TestMethod]
        public void GetTopN_OrdersByDescendingCosineSimilarity()
        {
            var query = new float[] { 1f, 0f };
            var reviews = new List<ReviewRecord>
            {
                new ReviewRecord { ReviewId = 1, Embedding = new float[] { 0f, 1f } },
                new ReviewRecord { ReviewId = 2, Embedding = new float[] { 1f, 0f } },
                new ReviewRecord { ReviewId = 3, Embedding = new float[] { 0.5f, 0.5f } }
            };
            var ranker = new SimilarityRanker();

            var result = ranker.GetTopN(reviews, query, 3);

            CollectionAssert.AreEqual(new[] { 2, 3, 1 }, result.Select(r => r.ReviewId).ToList());
        }

        [TestMethod]
        public void GetTopN_LimitsToRequestedCount()
        {
            var query = new float[] { 1f, 0f };
            var reviews = new List<ReviewRecord>
            {
                new ReviewRecord { ReviewId = 1, Embedding = new float[] { 1f, 0f } },
                new ReviewRecord { ReviewId = 2, Embedding = new float[] { 0.9f, 0.1f } },
                new ReviewRecord { ReviewId = 3, Embedding = new float[] { 0.8f, 0.2f } }
            };
            var ranker = new SimilarityRanker();

            var result = ranker.GetTopN(reviews, query, 2);

            Assert.AreEqual(2, result.Count);
            CollectionAssert.AreEqual(new[] { 1, 2 }, result.Select(r => r.ReviewId).ToList());
        }

        [TestMethod]
        public void GetTopN_IgnoresReviewsWithoutEmbeddings()
        {
            var query = new float[] { 1f, 0f };
            var reviews = new List<ReviewRecord>
            {
                new ReviewRecord { ReviewId = 1, Embedding = null },
                new ReviewRecord { ReviewId = 2, Embedding = new float[] { 1f, 0f } }
            };
            var ranker = new SimilarityRanker();

            var result = ranker.GetTopN(reviews, query, 5);

            CollectionAssert.AreEqual(new[] { 2 }, result.Select(r => r.ReviewId).ToList());
        }

        [TestMethod]
        public void GetTopN_IgnoresReviewsWithMismatchedEmbeddingLength()
        {
            var query = new float[] { 1f, 0f };
            var reviews = new List<ReviewRecord>
            {
                new ReviewRecord { ReviewId = 1, Embedding = new float[] { 1f, 0f } },
                new ReviewRecord { ReviewId = 2, Embedding = new float[] { 1f, 0f, 0f } }
            };
            var ranker = new SimilarityRanker();

            var result = ranker.GetTopN(reviews, query, 5);

            CollectionAssert.AreEqual(new[] { 1 }, result.Select(r => r.ReviewId).ToList());
        }
    }
}
