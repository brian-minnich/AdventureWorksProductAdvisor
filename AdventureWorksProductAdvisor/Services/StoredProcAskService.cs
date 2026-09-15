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

        public StoredProcAskService(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<AskServiceResult> AskAsync(string question, int maxCompletionTokens, int topN)
        {
            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand("dbo.AskProductQuestion", connection))
            {
                command.CommandType = CommandType.StoredProcedure;
                command.Parameters.AddWithValue("@Question", question);
                command.Parameters.AddWithValue("@MaxTokens", maxCompletionTokens);
                command.Parameters.AddWithValue("@TopN", topN);
                var answerParam = command.Parameters.Add("@Answer", SqlDbType.NVarChar, -1);
                answerParam.Direction = ParameterDirection.Output;

                await connection.OpenAsync();
                // The procedure has no result set - it only ever SETs
                // @Answer - so ExecuteNonQueryAsync is correct here; there's
                // no reader to read.
                await command.ExecuteNonQueryAsync();

                return new AskServiceResult { Success = true, Answer = answerParam.Value as string };
            }
        }
    }
}
