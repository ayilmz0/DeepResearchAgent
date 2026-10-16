
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DeepResearchAgent.Infrastructure.Services.Crawling;
using Xunit;

namespace DeepResearchAgent.Tests;

public class WebCrawlerRedirectTests
{
    [Theory]
    [InlineData("application/pdf")]
    [InlineData("application/json")]
    [InlineData("text/plain")]
    public async Task CrawlAsync_NonHtmlContentType_Throws(
        string contentType)
    {
        var handler = new NonHtmlResponseHandler(contentType);

        using var httpClient = new HttpClient(handler);
        var crawler = new WebCrawler(httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => crawler.CrawlAsync("https://8.8.8.8/"));

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task CrawlAsync_ContentExceeds5MB_Throws()
    {
        var handler = new LargeHtmlResponseHandler();

        using var httpClient = new HttpClient(handler);
        var crawler = new WebCrawler(httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => crawler.CrawlAsync("https://8.8.8.8/"));

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task CrawlAsync_RedirectsToLocalhost_RejectsRedirect()
    {
        var handler = new RedirectToLocalhostHandler();

        using var httpClient = new HttpClient(handler);
        var crawler = new WebCrawler(httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => crawler.CrawlAsync("https://8.8.8.8/"));

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task CrawlAsync_TooManyRedirects_Throws()
    {
        var handler = new RedirectLoopHandler();

        using var httpClient = new HttpClient(handler);
        var crawler = new WebCrawler(httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => crawler.CrawlAsync("https://8.8.8.8/"));

        Assert.Equal(6, handler.RequestCount);
    }

    [Fact]
    public async Task CrawlAsync_CanceledBeforeRequest_ThrowsOperationCanceled()
    {
        var handler = new NonHtmlResponseHandler("text/html");

        using var httpClient = new HttpClient(handler);
        var crawler = new WebCrawler(httpClient);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => crawler.CrawlAsync(
                "https://8.8.8.8/",
                cts.Token));

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task CrawlAsync_CanceledDuringStreamRead_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        var handler = new CancelDuringReadHandler(cts);

        using var httpClient = new HttpClient(handler);
        var crawler = new WebCrawler(httpClient);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => crawler.CrawlAsync(
                "https://8.8.8.8/",
                cts.Token));

        Assert.Equal(1, handler.RequestCount);
    }

    private sealed class NonHtmlResponseHandler : HttpMessageHandler
    {
        private readonly string _contentType;

        public int RequestCount { get; private set; }

        public NonHtmlResponseHandler(string contentType)
        {
            _contentType = contentType;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;

            var content = new StringContent("test response");
            content.Headers.ContentType =
                new MediaTypeHeaderValue(_contentType);

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = content
                });
        }
    }

    private sealed class LargeHtmlResponseHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;

            var html =
                "<html><body>" +
                new string('A', 5 * 1024 * 1024 + 1) +
                "</body></html>";

            var content = new UnknownLengthContent(
                Encoding.UTF8.GetBytes(html));

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = content
                });
        }
    }

    private sealed class RedirectLoopHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers =
                    {
                        Location = new Uri("https://8.8.8.8/next")
                    }
                });
        }
    }

    private sealed class RedirectToLocalhostHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers =
                    {
                        Location = new Uri("http://127.0.0.1/")
                    }
                });
        }
    }

    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] _data;

        public UnknownLengthContent(byte[] data)
        {
            _data = data;
            Headers.ContentType =
                new MediaTypeHeaderValue("text/html");
        }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            System.Net.TransportContext? context)
        {
            return stream.WriteAsync(_data, 0, _data.Length);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class CancelDuringReadHandler : HttpMessageHandler
    {
        private readonly CancellationTokenSource _cts;

        public int RequestCount { get; private set; }

        public CancelDuringReadHandler(CancellationTokenSource cts)
        {
            _cts = cts;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;

            var content = new CancelDuringReadContent(_cts);

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = content
                });
        }
    }

    private sealed class CancelDuringReadContent : HttpContent
    {
        private readonly CancellationTokenSource _cts;

        public CancelDuringReadContent(CancellationTokenSource cts)
        {
            _cts = cts;
            Headers.ContentType =
                new MediaTypeHeaderValue("text/html");
        }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            System.Net.TransportContext? context)
        {
            return Task.CompletedTask;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            return Task.FromResult<Stream>(
                new CancelDuringReadStream(_cts));
        }
    }

    private sealed class CancelDuringReadStream : Stream
    {
        private readonly CancellationTokenSource _cts;
        private bool _firstRead = true;

        public CancelDuringReadStream(CancellationTokenSource cts)
        {
            _cts = cts;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_firstRead)
            {
                _firstRead = false;

                var html = Encoding.UTF8.GetBytes(
                    "<html><body>Test</body></html>");

                html.AsMemory().CopyTo(buffer);

                return ValueTask.FromResult(html.Length);
            }

            // İkinci okumada araştırmayı gerçekten iptal et.
            _cts.Cancel();

            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult(0);
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
        {
            throw new NotSupportedException();
        }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(
            byte[] buffer,
            int offset,
            int count)
        {
            throw new NotSupportedException();
        }
    }
}
