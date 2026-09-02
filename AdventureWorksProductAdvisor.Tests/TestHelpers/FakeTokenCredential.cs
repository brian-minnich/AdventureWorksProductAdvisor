using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;

namespace AdventureWorksProductAdvisor.Tests.TestHelpers
{
    // Stands in for DefaultAzureCredential in tests. TokenCredential is an
    // abstract class from the Azure SDK - anything that can hand back an
    // access token can be one, so this fake just returns a fixed string
    // immediately instead of doing a real Azure AD login. This is what lets
    // the Azure*Service tests run offline with no real identity involved.
    public class FakeTokenCredential : TokenCredential
    {
        private readonly string _token;

        public FakeTokenCredential(string token = "fake-token")
        {
            _token = token;
        }

        // Both the sync and async token-fetch methods need overriding since
        // they're both abstract on the base class - production code
        // (AzureOpenAiEmbeddingService/AzureOpenAiChatCompletionService)
        // only ever calls the async one, but this keeps the fake complete.
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            return new AccessToken(_token, DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            return new ValueTask<AccessToken>(GetToken(requestContext, cancellationToken));
        }
    }
}
