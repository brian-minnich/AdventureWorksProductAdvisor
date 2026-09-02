using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AdventureWorksProductAdvisor.Tests.TestHelpers
{
    // HttpClient never actually sends anything itself - it delegates to an
    // HttpMessageHandler underneath it. This class is a hand-written stand-in
    // for that handler: instead of opening a real socket, it just runs
    // whatever function the test gave it (_responder) and returns that
    // instead. Pass one of these into `new HttpClient(handler)` and every
    // call that HttpClient makes is intercepted here.
    //
    // Used by both AzureOpenAiEmbeddingServiceTests and
    // AzureOpenAiChatCompletionServiceTests, since both services make HTTP
    // calls the same way.
    public class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        // The handler records the last request it saw so a test can inspect
        // it afterward - e.g. to confirm the URL or headers were built correctly.
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
