namespace LocalMateAI.Infrastructure.Payments;

public sealed class PayOSGatewayOptions
{
    public const string SectionName = "PayOS";

    public string ClientId { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string ChecksumKey { get; init; } = string.Empty;
    public string ReturnUrl { get; init; } = string.Empty;
    public string CancelUrl { get; init; } = string.Empty;

    public bool IsComplete() =>
        !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ChecksumKey)
        && IsHttpUrl(ReturnUrl)
        && IsHttpUrl(CancelUrl);

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
