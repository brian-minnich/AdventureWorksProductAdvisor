using System;
using System.Collections.Generic;
using System.Configuration;
using System.Threading.Tasks;
using System.Web.Http;
using AdventureWorksProductAdvisor.Models;
using AdventureWorksProductAdvisor.Services;

namespace AdventureWorksProductAdvisor.Controllers.Api
{
    [RoutePrefix("api/ask")]
    public class AskController : ApiController
    {
        private readonly IReadOnlyDictionary<string, IAskService> _pipelinesByMode;
        private readonly int _maxCompletionTokens;

        public AskController()
            : this(AskServiceFactory.CreatePipelines(), int.Parse(ConfigurationManager.AppSettings["MaxCompletionTokens"]))
        {
        }

        public AskController(IReadOnlyDictionary<string, IAskService> pipelinesByMode, int maxCompletionTokens)
        {
            _pipelinesByMode = pipelinesByMode;
            _maxCompletionTokens = maxCompletionTokens;
        }

        [HttpPost]
        [Route("")]
        public async Task<IHttpActionResult> Post(AskRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Question))
            {
                return Ok(new AskResponse { Success = false, ErrorMessage = "Question is required." });
            }

            var topN = request.TopN ?? 5;
            if (topN < 1 || topN > 10)
            {
                return Ok(new AskResponse { Success = false, Mode = request.Mode, ErrorMessage = "TopN must be between 1 and 10." });
            }

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
                var result = await pipeline.AskAsync(request.Question, _maxCompletionTokens, topN);
                return Ok(new AskResponse
                {
                    Success = result.Success,
                    Answer = result.Answer,
                    Mode = request.Mode
                });
            }
            catch (Exception)
            {
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
