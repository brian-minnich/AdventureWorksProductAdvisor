using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace AdventureWorksProductAdvisor.Data
{
    public class ReviewRepository : IReviewRepository
    {
        private readonly string _connectionString;

        public ReviewRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<List<ReviewRecord>> GetAllReviewsAsync()
        {
            const string sql = @"
                SELECT r.ReviewID, r.ProductID, p.Name AS ProductName, r.Rating, r.ReviewTitle, r.ReviewText, r.ReviewEmbeddingJson
                FROM dbo.ProductReview r
                INNER JOIN SalesLT.Product p ON r.ProductID = p.ProductID";

            var results = new List<ReviewRecord>();
            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                await connection.OpenAsync();
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var embeddingJson = reader["ReviewEmbeddingJson"] as string;
                        results.Add(new ReviewRecord
                        {
                            ReviewId = (int)reader["ReviewID"],
                            ProductId = (int)reader["ProductID"],
                            ProductName = (string)reader["ProductName"],
                            Rating = (byte)reader["Rating"],
                            ReviewTitle = (string)reader["ReviewTitle"],
                            ReviewText = (string)reader["ReviewText"],
                            Embedding = string.IsNullOrEmpty(embeddingJson)
                                ? null
                                : JsonConvert.DeserializeObject<float[]>(embeddingJson)
                        });
                    }
                }
            }
            return results;
        }

        public async Task SaveEmbeddingAsync(int reviewId, float[] embedding)
        {
            const string sql = "UPDATE dbo.ProductReview SET ReviewEmbeddingJson = @Json WHERE ReviewID = @ReviewId";
            var json = JsonConvert.SerializeObject(embedding);
            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@Json", json);
                command.Parameters.AddWithValue("@ReviewId", reviewId);
                await connection.OpenAsync();
                await command.ExecuteNonQueryAsync();
            }
        }
    }
}
