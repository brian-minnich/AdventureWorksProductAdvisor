using System.Threading.Tasks;
using System.Web.Http;
using AdventureWorksProductAdvisor.Services;

namespace AdventureWorksProductAdvisor.Controllers.Api
{
    // Thin HTTP wrapper around ReviewEmbeddingBackfillRunner - all it does
    // is trigger the run and return how many reviews got embedded. There is
    // no authentication on this endpoint; that's acceptable for this
    // personal, single-user project, but would need to change before ever
    // deploying this publicly, since anyone who found the URL could trigger
    // (and pay for) a re-embedding run.
    [RoutePrefix("api/admin")]
    public class AdminController : ApiController
    {
        private readonly ReviewEmbeddingBackfillRunner _runner;

        // Real runtime constructor - builds the runner (real DB, real Azure
        // OpenAI) via the composition root.
        public AdminController()
            : this(AskServiceFactory.CreateBackfillRunner())
        {
        }

        // Testable constructor - AdminControllerTests passes in a runner
        // built from mocked IReviewRepository/IEmbeddingService instead.
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
