namespace AdventureWorksProductAdvisor.Models
{
    public class AskRequest
    {
        public string Question { get; set; }
        public string Mode { get; set; }
        public int? TopN { get; set; }
    }
}
