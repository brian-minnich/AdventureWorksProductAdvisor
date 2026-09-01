using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AdventureWorksProductAdvisor.Tests.TestHelpers
{
    public class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public HttpRequestMessage LastRequest { get; private set; }
        public string LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content == null ? null : await request.Content.ReadAsStringAsync();
            return _responder(request);
        }
    }
}
