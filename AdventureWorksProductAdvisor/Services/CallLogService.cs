using System;
using System.Data.SqlClient;
using System.Threading.Tasks;
using AdventureWorksProductAdvisor.Data;
using AdventureWorksProductAdvisor.Models;

namespace AdventureWorksProductAdvisor.Services
{
    // Plain ADO.NET, same style as ReviewRepository - no ORM. Not unit
    // tested (see the plan's Global Constraints) - exercised via manual
    // testing against the real Azure SQL database.
    public class CallLogService : ICallLogService
    {
        private readonly string _connectionString;

        public CallLogService(string connectionString)
        {
            _connectionString = connectionString;
        }

        // No SqlRetry here, deliberately: AskController's daily-cap check
        // already fails open on ANY exception from this call (a broken
        // CallLog table shouldn't block real Q&A) and treats it as
        // under-the-limit. Retrying first would just add several seconds
        // of delay in front of that existing fast fail-open path, on
        // every request, for the entire time the database is unavailable.
        public async Task<int> GetTodaysCallCountAsync()
        {
            const string sql = "SELECT COUNT(*) FROM dbo.CallLog WHERE CallTimestamp >= @TodayStartUtc";
            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                // Computed here (not in T-SQL) so "today" is unambiguously
                // UTC-calendar-day, matching CallTimestamp's own UTC convention.
                command.Parameters.AddWithValue("@TodayStartUtc", DateTime.UtcNow.Date);
                await connection.OpenAsync();
                return (int)await command.ExecuteScalarAsync();
            }
        }

        public async Task LogCallAsync(CallLogEntry entry)
        {
            const string sql = @"
                INSERT INTO dbo.CallLog
                    (CallTimestamp, Mode, Question, MaxCompletionTokens, EstimatedInputTokens, EstimatedOutputTokens, Success, ErrorMessage)
                VALUES
                    (@CallTimestamp, @Mode, @Question, @MaxCompletionTokens, @EstimatedInputTokens, @EstimatedOutputTokens, @Success, @ErrorMessage)";
            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@CallTimestamp", entry.CallTimestamp);
                command.Parameters.AddWithValue("@Mode", entry.Mode);
                command.Parameters.AddWithValue("@Question", entry.Question);
                command.Parameters.AddWithValue("@MaxCompletionTokens", entry.MaxCompletionTokens);
                // AddWithValue can't take a C# null directly for a nullable
                // int - it has to be boxed then coalesced to DBNull.Value,
                // or SqlClient throws.
                command.Parameters.AddWithValue("@EstimatedInputTokens", (object)entry.EstimatedInputTokens ?? DBNull.Value);
                command.Parameters.AddWithValue("@EstimatedOutputTokens", (object)entry.EstimatedOutputTokens ?? DBNull.Value);
                command.Parameters.AddWithValue("@Success", entry.Success);
                command.Parameters.AddWithValue("@ErrorMessage", (object)entry.ErrorMessage ?? DBNull.Value);
                // Retries only the connect (see SqlRetry) - once the INSERT
                // itself starts, a failure is left to propagate rather than
                // being retried, so a transient error can't cause the same
                // call to be logged twice.
                await SqlRetry.OpenAsync(connection);
                await command.ExecuteNonQueryAsync();
            }
        }
    }
}
