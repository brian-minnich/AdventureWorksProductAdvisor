using AdventureWorksProductAdvisor.Data;

namespace AdventureWorksProductAdvisor.Services
{
    // SimilarityRanker.GetTopN's result shape - pairs each retrieved review
    // with the cosine similarity score it was ranked by. Callers that only
    // want the reviews (to build a prompt) can still just read .Review; the
    // score is what lets CSharpAskService tell "closely matched" apart from
    // "nothing relevant was found" instead of only having the review text.
    public class RankedReview
    {
        public ReviewRecord Review { get; set; }
        public double Score { get; set; }
    }
}
