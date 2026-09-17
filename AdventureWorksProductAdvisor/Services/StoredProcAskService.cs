using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace AdventureWorksProductAdvisor.Services
{
    // The stored-procedure-backed twin of CSharpAskService - same
    // IAskService contract, but the entire embed -> retrieve -> rank ->
    // prompt -> call -> parse pipeline happens inside dbo.AskProductQuestion
    // instead of in C#. No try/catch here: SQL exceptions (bad connection,
    // timeout, the procedure itself erroring) propagate to AskController's
    // existing catch block, exactly like unhandled exceptions from
    // CSharpAskService do today. Not unit tested - see the plan's Global
    // Constraints.
    public class StoredProcAskService : IAskService
    {
        private readonly string _connectionString;

        // Same guardrail as CSharpAskService's minSimilarityScore, passed
        // through to dbo.AskProductQuestion's own @MinSimilarityScore
        // parameter (see Sql/dbo.AskProductQuestion.sql) rather than
        // relying on that parameter's own default - AskServiceFactory feeds
        // both pipelines the same Web.config value, so the two can't drift
        // out of sync with each other.
        private readonly double _minSimilarityScore;

        public StoredProcAskService(string connectionString, double minSimilarityScore = 0.35)
        {
            _connectionString = connectionString;
            _minSimilarityScore = minSimilarityScore;
        }

        public async Task<AskServiceResult> AskAsync(string question, int maxCompletionTokens, int topN)
        {
            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand("dbo.AskProductQuestion", connection))
            {
                command.CommandType = CommandType.StoredProcedure;
                // 100 seconds, matching HttpClient's 100-second default used
                // by the C# pipeline (AzureOpenAiChatCompletionService /
                // AzureOpenAiEmbeddingService), so both modes are bounded by
                // comparable timeouts for a fair side-by-side comparison.
                command.CommandTimeout = 100;
                command.Parameters.AddWithValue("@Question", question);
                command.Parameters.AddWithValue("@MaxTokens", maxCompletionTokens);
                command.Parameters.AddWithValue("@TopN", topN);
                command.Parameters.AddWithValue("@MinSimilarityScore", _minSimilarityScore);
                var answerParam = command.Parameters.Add("@Answer", SqlDbType.NVarChar, -1);
                answerParam.Direction = ParameterDirection.Output;

                await connection.OpenAsync();
                // The procedure has no result set - it only ever SETs
                // @Answer - so ExecuteNonQueryAsync is correct here; there's
                // no reader to read.
                await command.ExecuteNonQueryAsync();

                var answer = answerParam.Value as string;
                return new AskServiceResult { Success = !IsProcedureError(answer), Answer = answer };
            }
        }

        // dbo.AskProductQuestion never raises a SQL error for an
        // sp_invoke_external_rest_endpoint failure (429, 401/403, or any
        // other non-zero status) - it catches those itself and puts a
        // human-readable message straight into @Answer (see
        // sql/dbo.AskProductQuestion.sql). Without this check, those
        // failures looked identical to a real answer: Success = true, shown
        // in the UI as a normal response, and logged to CallLog as a
        // successful call. The three prefixes below are the procedure's own
        // error strings; "No reviews found..." is deliberately NOT treated
        // as an error here, matching CSharpAskService's equivalent
        // no-matching-reviews case, which is also Success = true.
        private static bool IsProcedureError(string answer)
        {
            if (answer == null)
            {
                return false;
            }
            return answer.StartsWith("The service is currently busy.")
                || answer.StartsWith("Authentication failed.")
                || answer.StartsWith("Unable to process your question at this time.");
        }
    }
}
