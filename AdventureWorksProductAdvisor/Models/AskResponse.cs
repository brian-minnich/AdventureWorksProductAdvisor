namespace AdventureWorksProductAdvisor.Models
{
    // The shape Web API serializes back to the browser as JSON. This is
    // deliberately a different type from AskServiceResult (the pipeline's
    // internal result) - AskController is what maps between the two, adding
    // Mode/ErrorMessage which the pipeline layer itself has no need to know about.
    public class AskResponse
    {
        // True if the pipeline produced an answer at all (even "no reviews
        // matched" counts as Success: true) - false for validation failures,
        // an unimplemented mode, or an exception. The browser checks this,
        // not the HTTP status code, which is always 200 either way.
        public bool Success { get; set; }
        public string Answer { get; set; }
        // Echoed back so the UI can label which pipeline produced this answer.
        public string Mode { get; set; }
        public string ErrorMessage { get; set; }
    }
}
