using System.Threading.Tasks;
using System.Web.Http;
using AdventureWorksProductAdvisor.Services;

namespace AdventureWorksProductAdvisor.Controllers.Api
{
    [RoutePrefix("api/admin")]
    public class AdminController : ApiController
    {
        private readonly ReviewEmbeddingBackfillRunner _runner;

        public AdminController()
            : this(new ReviewEmbeddingBackfillRunner(AskServiceFactory.CreateReviewRepository(), AskServiceFactory.CreateEmbeddingService()))
        {
        }

        public AdminController(ReviewEmbeddingBackfillRunner runner)
        {
            _runner = runner;
        }

        [HttpPost]
        [Route("backfill-embeddings")]
        public async Task<IHttpActionResult> BackfillEmbeddings()
        {
            var count = await _runner.RunAsync();
            return Ok(count);
        }
    }
}
