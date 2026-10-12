namespace DeepResearchAgent.Engine.Helpers;

public static class UrlNormalizer
{
    public static string Normalize(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url.Trim();

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.Host.ToLowerInvariant(),
            Fragment = string.Empty
        };

        // Varsayılan portları kaldır.
        if ((builder.Scheme == "https" && builder.Port == 443) ||
            (builder.Scheme == "http" && builder.Port == 80))
        {
            builder.Port = -1;
        }

        var normalized = builder.Uri.AbsoluteUri;

        // Sondaki slash'i kaldır; kök adresi koru.
        if (normalized.EndsWith("/") &&
            builder.Path != "/")
        {
            normalized = normalized.TrimEnd('/');
        }

        return normalized;
    }
}
