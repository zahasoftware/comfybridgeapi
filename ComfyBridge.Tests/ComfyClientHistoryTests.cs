using System.Net;
using System.Text;
using ComfyBridge.Infrastructure.Comfy;
using ComfyBridge.Infrastructure.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ComfyBridge.Tests;

public sealed class ComfyClientHistoryTests
{
    [Fact]
    public async Task WaitForResultAsync_IncludesVideoAndGifAssetUrls()
    {
        var historyPayload = """
        {
          "prompt-1": {
            "outputs": {
              "130:117": {
                "videos": [
                  {
                    "filename": "sample.mp4",
                    "subfolder": "video",
                    "type": "output"
                  }
                ],
                "gifs": [
                  {
                    "filename": "sample.gif",
                    "subfolder": "video",
                    "type": "output"
                  }
                ],
                "images": [
                  {
                    "filename": "frame.png",
                    "subfolder": "video",
                    "type": "output"
                  }
                ]
              }
            }
          }
        }
        """;

        using var httpClient = new HttpClient(new StubHttpMessageHandler(historyPayload))
        {
            BaseAddress = new Uri("http://127.0.0.1:8188/")
        };

        var options = Options.Create(new ComfyUiOptions
        {
            BaseUrl = "http://127.0.0.1:8188",
            PollIntervalMs = 1,
            JobTimeoutSeconds = 5
        });

        var client = new ComfyClient(httpClient, options, NullLogger<ComfyClient>.Instance);

        var result = await client.WaitForResultAsync("prompt-1", TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.Contains(result.AssetUrls, u => u.Contains("filename=sample.mp4", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.AssetUrls, u => u.Contains("filename=sample.gif", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.AssetUrls, u => u.Contains("filename=frame.png", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class StubHttpMessageHandler(string historyPayload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get && request.RequestUri is not null && request.RequestUri.AbsolutePath.Contains("history/", StringComparison.OrdinalIgnoreCase))
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(historyPayload, Encoding.UTF8, "application/json")
                };

                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
