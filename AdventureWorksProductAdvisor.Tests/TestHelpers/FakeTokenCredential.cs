using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;

namespace AdventureWorksProductAdvisor.Tests.TestHelpers
{
    public class FakeTokenCredential : TokenCredential
    {
        private readonly string _token;

        public FakeTokenCredential(string token = "fake-token")
        {
            _token = token;
        }

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
