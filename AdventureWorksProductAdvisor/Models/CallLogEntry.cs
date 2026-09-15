using System;

namespace AdventureWorksProductAdvisor.Models
{
    // One row of dbo.CallLog - written after every pipeline call attempt
    // (success or failure) so usage is auditable per pipeline and per day.
    // EstimatedInputTokens/EstimatedOutputTokens stay null in this slice;
    // neither pipeline computes them yet.
    public class CallLogEntry
    {
        public DateTime CallTimestamp { get; set; }
        public string Mode { get; set; }
        public string Question { get; set; }
        public int MaxCompletionTokens { get; set; }
        public int? EstimatedInputTokens { get; set; }
        public int? EstimatedOutputTokens { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
    }
}
