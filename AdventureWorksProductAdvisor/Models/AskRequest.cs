namespace AdventureWorksProductAdvisor.Models
{
    // This is the shape Web API deserializes the incoming JSON POST body
    // into (see chat-widget.js's fetch call and AskController.Post). Note
    // there is deliberately no MaxCompletionTokens property here - that
    // value is a cost guard that only ever comes from Web.config on the
    // server, never from the client.
    public class AskRequest
    {
        public string Question { get; set; }
        // "CSharp" or "StoredProc" - looked up in AskController's
        // pipelines-by-mode dictionary.
        public string Mode { get; set; }
        // Nullable so a request that omits TopN can be told apart from one
        // that explicitly sends 0 - AskController defaults a missing value
        // to 5 (`request.TopN ?? 5`).
        public int? TopN { get; set; }
    }
}
