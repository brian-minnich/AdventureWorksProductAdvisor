using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace AdventureWorksProductAdvisor.Data
{
    // Plain ADO.NET - no Entity Framework or other ORM. This is the only
    // class that knows any SQL in the whole app; everything else works with
    // ReviewRecord objects.
    public class ReviewRepository : IReviewRepository
    {
        private readonly string _connectionString;

        public ReviewRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<List<ReviewRecord>> GetAllReviewsAsync()
        {
            // Joins across into SalesLT.Product just to bring back the
            // product's name alongside each review - ProductReview itself
            // only stores a ProductID.
            const string sql = @"
                SELECT r.ReviewID, r.ProductID, p.Name AS ProductName, r.Rating, r.ReviewTitle, r.ReviewText, r.ReviewEmbeddingJson
                FROM dbo.ProductReview r
                INNER JOIN SalesLT.Product p ON r.ProductID = p.ProductID";

            var results = new List<ReviewRecord>();
            // The three `using` blocks make sure the connection, command,
            // and reader all get closed/disposed even if something throws
            // partway through reading.
            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                await SqlRetry.OpenAsync(connection);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        // ReviewEmbeddingJson is NULL until the backfill has
                        // run for this row (see ReviewEmbeddingBackfillRunner),
                        // so it has to be checked before trying to parse it -
                        // this is what makes Embedding null on ReviewRecord
                        // for reviews that haven't been embedded yet.
                        var embeddingJson = reader["ReviewEmbeddingJson"] as string;
                        results.Add(new ReviewRecord
                        {
                            ReviewId = (int)reader["ReviewID"],
                            ProductId = (int)reader["ProductID"],
                            // `as string`/`as byte?` return null for DBNull.Value
                            // instead of throwing InvalidCastException, so one
                            // row with an unexpectedly null column doesn't take
                            // down the whole read.
                            ProductName = reader["ProductName"] as string,
                            Rating = (reader["Rating"] as byte?) ?? 0,
                            ReviewTitle = reader["ReviewTitle"] as string,
                            ReviewText = reader["ReviewText"] as string,
                            // The embedding is stored as a JSON array of
                            // numbers in a text column (ReviewEmbeddingJson),
                            // not a native vector type - JsonConvert turns
                            // that JSON text back into a float[] here.
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
            // Mirror image of the read above: serialize the float[] back
            // into a JSON string before writing it to the same
            // ReviewEmbeddingJson column.
            const string sql = "UPDATE dbo.ProductReview SET ReviewEmbeddingJson = @Json WHERE ReviewID = @ReviewId";
            var json = JsonConvert.SerializeObject(embedding);
            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                // Parameters (@Json, @ReviewId) rather than string-building
                // the SQL - this is what protects against SQL injection.
                command.Parameters.AddWithValue("@Json", json);
                command.Parameters.AddWithValue("@ReviewId", reviewId);
                await SqlRetry.OpenAsync(connection);
                await command.ExecuteNonQueryAsync();
            }
        }
    }
}
