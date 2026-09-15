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
        // handle a given Mode ("CSharp" or "StoredProc"). Keeping it as a
        // dictionary means adding a new pipeline later is just adding a new
        // entry, not changing this controller.
        private readonly IReadOnlyDictionary<string, IAskService> _pipelinesByMode;

        // The token cap for the chat model. This is a cost guard, so it is
        // deliberately NOT something the caller can set via the request body
        // - see AskRequest, which has no MaxCompletionTokens field at all.
        private readonly int _maxCompletionTokens;

        // Records every call attempt (success or failure) and answers
        // "how many calls happened today" for the daily-cap check below.
        private readonly ICallLogService _callLogService;

        // The other cost guard: a hard ceiling on total calls/day across
        // both pipelines, checked before either one is ever invoked.
        private readonly int _dailyCallLimit;

        // This is the constructor ASP.NET actually uses at runtime. It builds
        // the real pipelines (real HTTP calls, real database) via
        // AskServiceFactory, and reads config from Web.config.
        public AskController()
            : this(AskServiceFactory.CreatePipelines(), GetMaxCompletionTokensFromConfig(), AskServiceFactory.CreateCallLogService(), GetDailyCallLimitFromConfig())
        {
        }

        // Falls back to the documented default (500) if the config key is
        // missing or gets mistyped during a config edit, rather than
        // crashing every single controller construction - since ASP.NET
        // builds a new AskController per request, an unguarded int.Parse
        // here would take down all of /api/ask on a bad config value.
        private static int GetMaxCompletionTokensFromConfig()
        {
            int value;
            return int.TryParse(ConfigurationManager.AppSettings["MaxCompletionTokens"], out value) ? value : 500;
        }

        // Same fallback reasoning as GetMaxCompletionTokensFromConfig above.
        private static int GetDailyCallLimitFromConfig()
        {
            int value;
            return int.TryParse(ConfigurationManager.AppSettings["DailyCallLimit"], out value) ? value : 200;
        }

        // This second constructor is the "seam" that makes the controller
        // unit-testable: a test can pass in fake IAskService/ICallLogService
        // objects and hardcoded config values instead of real config/network
        // dependencies, the same idea as AzureOpenAiEmbeddingService taking a
        // fake HttpClient. See AskControllerTests.cs for how it's used.
        public AskController(
            IReadOnlyDictionary<string, IAskService> pipelinesByMode,
            int maxCompletionTokens,
            ICallLogService callLogService,
            int dailyCallLimit)
        {
            _pipelinesByMode = pipelinesByMode;
            _maxCompletionTokens = maxCompletionTokens;
            _callLogService = callLogService;
            _dailyCallLimit = dailyCallLimit;
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

            // Daily cap check, before either pipeline is ever touched. If
            // the count-check read itself fails, fail open - a broken
            // CallLog table shouldn't block real Q&A - and treat it as
            // under the limit.
            int todaysCallCount;
            try
            {
                todaysCallCount = await _callLogService.GetTodaysCallCountAsync();
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
                todaysCallCount = 0;
            }

            if (todaysCallCount >= _dailyCallLimit)
            {
                return Ok(new AskResponse
                {
                    Success = false,
                    Mode = request.Mode,
                    ErrorMessage = "Daily call limit reached. Please try again tomorrow."
                });
            }

            // Look up which pipeline should handle this Mode. If the client
            // asks for a mode that isn't wired up, TryGetValue fails and we
            // return a clear message WITHOUT ever touching a real pipeline -
            // no wasted API calls, and nothing worth logging to CallLog.
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

            // AskServiceResult (Success/Answer only) is the pipeline's
            // internal shape. It gets mapped into AskResponse (the shape the
            // browser actually receives, adding Mode/ErrorMessage) below.
            // Keeping these as two separate types means the pipeline layer
            // doesn't need to know anything about HTTP.
            AskServiceResult result = null;
            Exception caughtException = null;
            try
            {
                result = await pipeline.AskAsync(request.Question, _maxCompletionTokens, topN);
            }
            catch (Exception ex)
            {
                // Anything from here down (network failure, Azure OpenAI
                // rejecting the request, a bad DB connection string, etc.)
                // lands here. We log the real exception server-side via
                // Trace so it's diagnosable, but we deliberately return a
                // generic message to the browser rather than ex.Message -
                // no need to expose internal details to the client.
                caughtException = ex;
                Trace.TraceError(ex.ToString());
            }

            // Every call attempt gets logged regardless of outcome, so
            // usage is auditable per pipeline and per day. CallLog is never
            // exposed to the browser, so the real exception detail (not the
            // generic client-facing message) is what gets stored.
            await LogCallSafeAsync(new CallLogEntry
            {
                CallTimestamp = DateTime.UtcNow,
                Mode = request.Mode,
                Question = request.Question,
                MaxCompletionTokens = _maxCompletionTokens,
                Success = caughtException == null,
                ErrorMessage = caughtException?.Message
            });

            if (caughtException != null)
            {
                return Ok(new AskResponse
                {
                    Success = false,
                    Mode = request.Mode,
                    ErrorMessage = "An error occurred while processing your question. Please try again."
                });
            }

            return Ok(new AskResponse
            {
                Success = result.Success,
                Answer = result.Answer,
                Mode = request.Mode
            });
        }

        // A CallLog write failure should never turn an already-completed
        // pipeline call into a user-facing error - the point of CallLog is
        // auditability, not gatekeeping a call that already happened (and
        // was already billed, if it succeeded).
        private async Task LogCallSafeAsync(CallLogEntry entry)
        {
            try
            {
                await _callLogService.LogCallAsync(entry);
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
            }
        }
    }
}
