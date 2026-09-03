using System;
using System.Collections.Generic;
using System.Linq;
using AdventureWorksProductAdvisor.Data;

namespace AdventureWorksProductAdvisor.Services
{
    // Unlike everything else in this pipeline, this class does no I/O at
    // all - no network, no database, just math over numbers already in
    // memory. That's why SimilarityRankerTests.cs needs no fakes/mocks: you
    // can just hand it plain float arrays and check the answer directly.
    public class SimilarityRanker
    {
        // Given a query embedding (the question, turned into a vector) and a
        // list of reviews (each with its own pre-computed embedding), returns
        // the topN reviews whose embeddings are most similar to the query.
        public List<ReviewRecord> GetTopN(IEnumerable<ReviewRecord> reviews, float[] queryEmbedding, int topN)
        {
            return reviews
                // Skip reviews that were never embedded (Embedding is null
                // until the backfill runs) or whose embedding is a different
                // length than the query's - comparing vectors of different
                // lengths isn't meaningful, so those are silently excluded
                // rather than throwing.
                .Where(r => r.Embedding != null && r.Embedding.Length == queryEmbedding.Length)
                // Score every remaining review by how similar it is to the question.
                .Select(r => new { Review = r, Score = CosineSimilarity(r.Embedding, queryEmbedding) })
                // Highest similarity first...
                .OrderByDescending(x => x.Score)
                // ...and keep only as many as the caller asked for.
                .Take(topN)
                .Select(x => x.Review)
                .ToList();
        }

        // Cosine similarity measures the angle between two vectors, not
        // their length - so it answers "do these two pieces of text point in
        // the same semantic direction?" regardless of how confidently-worded
        // either one is. Result ranges from -1 (opposite) to 1 (identical
        // direction); in practice, embeddings from the same model tend to
        // land somewhere in the 0-1 range for related text.
        private static double CosineSimilarity(float[] a, float[] b)
        {
            double dot = 0, magA = 0, magB = 0;
            for (var i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                magA += a[i] * a[i];
                magB += b[i] * b[i];
            }
            // Guards against dividing by zero if either vector is all zeros
            // (which shouldn't happen with real embeddings, but would crash
            // with a NaN result if it ever did).
            if (magA == 0 || magB == 0)
            {
                return 0;
            }
            return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
        }
    }
}
