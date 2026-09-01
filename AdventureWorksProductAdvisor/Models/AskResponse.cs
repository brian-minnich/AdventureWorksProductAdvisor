namespace AdventureWorksProductAdvisor.Models
{
    public class AskResponse
    {
        public bool Success { get; set; }
        public string Answer { get; set; }
        public string Mode { get; set; }
        public string ErrorMessage { get; set; }
    }
}
