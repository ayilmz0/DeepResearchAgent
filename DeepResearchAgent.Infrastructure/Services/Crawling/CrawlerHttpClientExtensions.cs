using System.Net;
using DeepResearchAgent.Engine.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Net.Sockets;

namespace DeepResearchAgent.Infrastructure.Services.Crawling;

public static class CrawlerHttpClientExtensions
{
    public static IHttpClientBuilder AddCrawlerHttpClient(
        this IServiceCollection services)
    {
        return services
            .AddHttpClient<ICrawler, WebCrawler>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);

                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "DeepResearchAgent/1.0");

                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("text/html"));

                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue(
                        "application/xhtml+xml"));
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
                new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false,

                    // Bağlantıyı uygulamanın kontrol etmesini sağla.
                    // Böylece sistem proxy'si üzerinden güvenlik
                    // kontrolünün atlanmasını önleriz.
                    UseProxy = false,

                    ConnectCallback = async (context, cancellationToken) =>
                    {
                        var endpoint = context.DnsEndPoint;

                        var addresses =
                            await CrawlerUrlValidator.ResolveSafeAddressesAsync(
                                endpoint.Host,
                                cancellationToken);

                        Exception? lastException = null;

                        foreach (var address in addresses)
                        {
                            var socket = new Socket(
                                address.AddressFamily,
                                SocketType.Stream,
                                ProtocolType.Tcp);

                            try
                            {
                                await socket.ConnectAsync(
                                    new IPEndPoint(address, endpoint.Port),
                                    cancellationToken);

                                // HttpClient, bu ham TCP akışının üzerinde
                                // HTTPS/TLS ve sertifika doğrulamasını
                                // normal şekilde gerçekleştirir.
                                return new NetworkStream(
                                    socket,
                                    ownsSocket: true);
                            }
                            catch (Exception ex)
                                when (ex is SocketException
                                      or OperationCanceledException)
                            {
                                socket.Dispose();

                                if (ex is OperationCanceledException)
                                    throw;

                                lastException = ex;
                            }
                            catch
                            {
                                socket.Dispose();
                                throw;
                            }
                        }

                        throw new HttpRequestException(
                            "Doğrulanan IP adreslerine bağlantı kurulamadı.",
                            lastException);
                    }
                });
    }
}
