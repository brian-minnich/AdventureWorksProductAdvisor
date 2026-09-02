using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Web.Http;
using AdventureWorksProductAdvisor.Models;
using AdventureWorksProductAdvisor.Services;

namespace AdventureWorksProductAdvisor.Controllers.Api
{
    // [RoutePrefix] + [Route("")] on Post() below means this whole class
    // answers POST requests to /api/ask (see WebApiConfig.Register, which
    // turns attribute routing on).
    [RoutePrefix("api/ask")]
    public class AskController : ApiController
    {
        // The "pipeline" is whichever IAskService implementation should
        // handle a given Mode ("CSharp" today; "StoredProc" is a future
        // slice). Keeping it as a dictionary means adding a new pipeline
        // later is just adding a new entry, not changing this controller.
        private readonly IReadOnlyDictionary<string, IAskService> _pipelinesByMode;

        // The token cap for the chat model. This is a cost guard, so it is
        // deliberately NOT something the caller can set via the request body
        // - see AskRequest, which has no MaxCompletionTokens field at all.
        private readonly int _maxCompletionTokens;

        // This is the constructor ASP.NET actually uses at runtime. It builds
        // the real pipelines (real HTTP calls, real database) via
        // AskServiceFactory, and reads the token cap from Web.config.
        public AskController()
            : this(AskServiceFactory.CreatePipelines(), int.Parse(ConfigurationManager.AppSettings["MaxCompletionTokens"]))
        {
        }

        // This second constructor is the "seam" that makes the controller
        // unit-testable: a test can pass in fake IAskService objects and a
        // hardcoded token cap instead of real config/network dependencies,
        // the same idea as AzureOpenAiEmbeddingService taking a fake
        // HttpClient. See AskControllerTests.cs for how it's used.
        public AskController(IReadOnlyDictionary<string, IAskService> pipelinesByMode, int maxCompletionTokens)
        {
            _pipelinesByMode = pipelinesByMode;
            _maxCompletionTokens = maxCompletionTokens;
        }

        [HttpPost]
        [Route("")]
        public async Task<IHttpActionResult> Post(AskRequest request)
        {
            // Basic input check. Ok(...) here still returns HTTP 200 - the
            // Success flag inside AskResponse is what tells the browser
            // whether the answer worked, not the HTTP status code.
            if (request == null || string.IsNullOrWhiteSpace(request.Question))
            {
                return Ok(new AskResponse { Success = false, ErrorMessage = "Question is required." });
            }

            // TopN is validated here on the server no matter what the UI
            // spinner's min/max say client-side, because a direct POST to
            // this endpoint (e.g. via curl) can send anything - the browser
            // UI is not a security boundary.
            var topN = request.TopN ?? 5;
            if (topN < 1 || topN > 10)
            {
                return Ok(new AskResponse { Success = false, Mode = request.Mode, ErrorMessage = "TopN must be between 1 and 10." });
            }

            // Look up which pipeline should handle this Mode. If the client
            // asks for a mode that isn't wired up yet (e.g. "StoredProc"),
            // TryGetValue fails and we return a clear message WITHOUT ever
            // touching a real pipeline - no wasted API calls for an unknown mode.
            IAskService pipeline;
            if (!_pipelinesByMode.TryGetValue(request.Mode ?? string.Empty, out pipeline))
            {
                return Ok(new AskResponse
                {
                    Success = false,
                    Mode = request.Mode,
                    ErrorMessage = $"Mode '{request.Mode}' is not yet implemented."
                });
            }

            try
            {
                // AskServiceResult (Success/Answer only) is the pipeline's
                // internal shape. Here it gets mapped into AskResponse, the
                // shape the browser actually receives (adds Mode/ErrorMessage).
                // Keeping these as two separate types means the pipeline layer
                // doesn't need to know anything about HTTP.
                var result = await pipeline.AskAsync(request.Question, _maxCompletionTokens, topN);
                return Ok(new AskResponse
                {
                    Success = result.Success,
                    Answer = result.Answer,
                    Mode = request.Mode
                });
            }
            catch (Exception ex)
            {
                // Anything from here down (network failure, Azure OpenAI
                // rejecting the request, a bad DB connection string, etc.)
                // lands here. We log the real exception server-side via
                // Trace so it's diagnosable, but we deliberately return a
                // generic message to the browser rather than ex.Message -
                // no need to expose internal details to the client.
                Trace.TraceError(ex.ToString());
                return Ok(new AskResponse
                {
                    Success = false,
                    Mode = request.Mode,
                    ErrorMessage = "An error occurred while processing your question. Please try again."
                });
            }
        }
    }
}
