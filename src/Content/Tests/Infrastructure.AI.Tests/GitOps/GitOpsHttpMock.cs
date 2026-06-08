using System.Net;
using Moq;
using Moq.Protected;

namespace Infrastructure.AI.Tests.GitOps;

/// <summary>
/// Helpers for wiring a mocked <see cref="HttpMessageHandler"/> behind the
/// egress-named <see cref="HttpClient"/> the GitOps API clients resolve via
/// <c>IHttpClientFactory.CreateClient(EgressPolicyDelegatingHandler.ClientName)</c>.
/// Mirrors the pattern in <c>A2AAgentHostTests</c>.
/// </summary>
internal static class GitOpsHttpMock
{
    /// <summary>
    /// Builds an <see cref="HttpClient"/> that returns the same status + JSON body
    /// for every request. Use when a single endpoint is hit.
    /// </summary>
    public static HttpClient JsonClient(HttpStatusCode statusCode, string json)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json)
            });

        return new HttpClient(handler.Object);
    }

    /// <summary>
    /// Builds an <see cref="HttpClient"/> that routes responses by request path
    /// substring. The first matching key wins; an unmatched request yields 404.
    /// Use when a single client calls multiple endpoints (e.g. Flux lists both
    /// kustomizations and helmreleases).
    /// </summary>
    public static HttpClient RoutedJsonClient(IReadOnlyDictionary<string, string> bodyByPathFragment)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
            {
                var url = req.RequestUri!.ToString();
                foreach (var (fragment, body) in bodyByPathFragment)
                {
                    if (url.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(body)
                        };
                    }
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

        return new HttpClient(handler.Object);
    }

    /// <summary>
    /// Builds an <see cref="HttpClient"/> whose handler throws
    /// <see cref="HttpRequestException"/> on every send — simulates an unreachable
    /// controller API.
    /// </summary>
    public static HttpClient UnreachableClient()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("simulated unreachable"));

        return new HttpClient(handler.Object);
    }
}
