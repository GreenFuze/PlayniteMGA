using MGA.Playnite.Api;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static MGA.Playnite.Tests.Harness;

namespace MGA.Playnite.Tests
{
    internal static class PagingTests
    {
        public static void Run()
        {
            Test("an empty page before the promised total aborts reconciliation", () => Refused(
                "{\"total\":2,\"games\":[{\"id\":\"a\"}]}", "{\"total\":2,\"games\":[]}"));
            Test("a changing total aborts reconciliation", () => Refused(
                "{\"total\":2,\"games\":[{\"id\":\"a\"}]}", "{\"total\":1,\"games\":[{\"id\":\"b\"}]}"));
            Test("a repeated page aborts instead of duplicating records", () => Refused(
                "{\"total\":2,\"games\":[{\"id\":\"a\"}]}", "{\"total\":2,\"games\":[{\"id\":\"a\"}]}"));
            Test("a missing game identity aborts instead of pretending the sync completed", () => Refused(
                "{\"total\":1,\"games\":[{\"title\":\"No identity\"}]}"));
            Test("a valid empty library completes", () => AssertEqual(0, Read("{\"total\":0,\"games\":[]}").Count, "empty library failed"));
            Test("server-clamped pages are followed to the advertised total", () => AssertEqual(2, Read(
                "{\"total\":2,\"games\":[{\"id\":\"a\"}]}", "{\"total\":2,\"games\":[{\"id\":\"b\"}]}").Count, "paging stopped early"));
        }

        private static List<GameDto> Read(params string[] pages)
        {
            using (var client = new MgaApiClient("http://localhost:8900", "fixture", new Pages(pages)))
                return client.GetAllGamesAsync(200, true, CancellationToken.None).GetAwaiter().GetResult();
        }

        private static void Refused(params string[] pages)
        {
            try { Read(pages); }
            catch (MgaApiException) { return; }
            throw new Exception("An incomplete library was allowed to reach reconciliation.");
        }

        private sealed class Pages : HttpMessageHandler
        {
            private readonly Queue<string> pages;
            public Pages(IEnumerable<string> pages) { this.pages = new Queue<string>(pages); }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (pages.Count == 0) throw new Exception("Unexpected additional page request.");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = new StringContent(pages.Dequeue(), Encoding.UTF8, "application/json") });
            }
        }
    }
}
