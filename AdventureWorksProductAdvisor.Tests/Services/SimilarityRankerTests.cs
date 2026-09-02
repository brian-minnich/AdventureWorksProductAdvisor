using System.Collections.Generic;
using System.Linq;
using AdventureWorksProductAdvisor.Data;
using AdventureWorksProductAdvisor.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AdventureWorksProductAdvisor.Tests.Services
{
    // No mocks anywhere in this file - SimilarityRanker has no external
    // dependencies (no network, no database), so these tests just hand it
    // plain float arrays and check what comes back.
    [TestClass]
    public class SimilarityRankerTests
    {
        [TestMethod]
        public void GetTopN_OrdersByDescendingCosineSimilarity()
        {
            // Query points straight along the first axis. Review 2's
            // embedding is identical to the query (most similar), review 3
            // is at a 45-degree angle (partially similar), review 1 is
            // perpendicular (least similar) - so the expected order is 2, 3, 1.
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
            // All three reviews are similar to the query, but only the top 2
            // (by similarity, not by list order) should be returned.
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
            // Review 1 has never been through the backfill (Embedding is
            // null) - it should be silently excluded, not throw.
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
            // Review 2's embedding has 3 numbers, but the query only has 2 -
            // this would be meaningless to compare, so it should be
            // excluded the same way a null embedding is. This scenario
            // matters most if the embedding model/deployment is ever
            // changed to one that produces a different-sized vector: without
            // this filter, comparing old and new embeddings together would
            // produce nonsense results instead of a clear error.
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
