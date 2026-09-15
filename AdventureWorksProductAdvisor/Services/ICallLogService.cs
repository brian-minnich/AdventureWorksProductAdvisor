using System.Threading.Tasks;
using AdventureWorksProductAdvisor.Models;

namespace AdventureWorksProductAdvisor.Services
{
    // The daily-cap check (GetTodaysCallCountAsync) and the audit log
    // (LogCallAsync) both live behind this one interface, so AskController
    // never needs to know it's talking to a real database - same idea as
    // IReviewRepository.
    public interface ICallLogService
    {
        Task<int> GetTodaysCallCountAsync();
        Task LogCallAsync(CallLogEntry entry);
    }
}
