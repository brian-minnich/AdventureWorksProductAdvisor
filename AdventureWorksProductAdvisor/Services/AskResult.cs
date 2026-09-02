namespace AdventureWorksProductAdvisor.Services
{
    // The pipeline layer's own result shape - deliberately separate from
    // Models.AskResponse (the HTTP-facing shape), which additionally
    // carries Mode/ErrorMessage. Keeping them separate means IAskService
    // implementations never need to know they're being called from a web
    // request at all.
    public class AskServiceResult
    {
        public bool Success { get; set; }
        public string Answer { get; set; }
    }
}
