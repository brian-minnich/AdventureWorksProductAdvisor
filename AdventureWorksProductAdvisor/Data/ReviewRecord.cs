namespace AdventureWorksProductAdvisor.Data
{
    public class ReviewRecord
    {
        public int ReviewId { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public byte Rating { get; set; }
        public string ReviewTitle { get; set; }
        public string ReviewText { get; set; }
        public float[] Embedding { get; set; }
    }
}
