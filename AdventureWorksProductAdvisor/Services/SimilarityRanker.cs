using System;
using System.Collections.Generic;
using System.Linq;
using AdventureWorksProductAdvisor.Data;

namespace AdventureWorksProductAdvisor.Services
{
    public class SimilarityRanker
    {
        public List<ReviewRecord> GetTopN(IEnumerable<ReviewRecord> reviews, float[] queryEmbedding, int topN)
        {
            return reviews
                .Where(r => r.Embedding != null && r.Embedding.Length == queryEmbedding.Length)
                .Select(r => new { Review = r, Score = CosineSimilarity(r.Embedding, queryEmbedding) })
                .OrderByDescending(x => x.Score)
                .Take(topN)
                .Select(x => x.Review)
                .ToList();
        }

        private static double CosineSimilarity(float[] a, float[] b)
        {
            double dot = 0, magA = 0, magB = 0;
            for (var i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                magA += a[i] * a[i];
                magB += b[i] * b[i];
            }
            if (magA == 0 || magB == 0)
            {
                return 0;
            }
            return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
        }
    }
}
