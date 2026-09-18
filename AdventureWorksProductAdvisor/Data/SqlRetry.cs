using System;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace AdventureWorksProductAdvisor.Data
{
    // Azure SQL throws these specific error numbers for conditions that
    // resolve themselves shortly after - most commonly 40613 ("Database
    // ... is not currently available") on the first connection after the
    // database has sat idle, while it's still spinning back up. Retrying
    // ONLY the connect step (not the command that follows) turns that into
    // a slower first request instead of a hard failure, without risking a
    // second attempt re-running a command that already had a side effect
    // (a paid external call, an INSERT) on a prior attempt - by the time a
    // command is running, the connection is known-good and any failure
    // from here on is left to propagate rather than being retried.
    public static class SqlRetry
    {
        private static readonly int[] TransientErrorNumbers = { 40613, 40197, 40501, 4060, 10928, 10929 };
        private static readonly TimeSpan[] DelaysBetweenAttempts = { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) };

        // Reopening the same SqlConnection instance after a failed
        // OpenAsync is a supported, documented pattern - no need to
        // dispose and recreate it between attempts.
        public static async Task OpenAsync(SqlConnection connection)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await connection.OpenAsync();
                    return;
                }
                catch (SqlException ex) when (attempt < DelaysBetweenAttempts.Length && IsTransient(ex))
                {
                    await Task.Delay(DelaysBetweenAttempts[attempt]);
                }
            }
        }

        private static bool IsTransient(SqlException ex)
        {
            foreach (SqlError error in ex.Errors)
            {
                if (Array.IndexOf(TransientErrorNumbers, error.Number) >= 0)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
