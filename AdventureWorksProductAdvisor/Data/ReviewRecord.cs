namespace AdventureWorksProductAdvisor.Data
{
    // Plain data holder for one row from dbo.ProductReview (joined with its
    // product's name) - what ReviewRepository.GetAllReviewsAsync returns,
    // and what SimilarityRanker/CSharpAskService work with. Nothing in this
    // class knows about SQL, HTTP, or JSON.
    public class ReviewRecord
    {
        public int ReviewId { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public byte Rating { get; set; }
        public string ReviewTitle { get; set; }
        public string ReviewText { get; set; }
        // Null until the one-time backfill (ReviewEmbeddingBackfillRunner)
        // has embedded this review - SimilarityRanker skips any review
        // where this is still null.
        public float[] Embedding { get; set; }
    }
}
