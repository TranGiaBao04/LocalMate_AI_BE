using System.Net;
using System.Text;
using LocalMateAI.API.Controllers;
using LocalMateAI.API.Middlewares;
using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;

namespace LocalMateAI.Tests;

public sealed class PaymentEvidenceHttpTests
{
    [Fact]
    public async Task Kestrel_ChunkedOversize_Returns413WithoutVerificationOrEvidence()
    {
        using var host = new Host(kestrel: true);
        using var body = new UnknownLengthContent(new string(' ', 65537));
        var response = await host.Client.PostAsync("/api/payments/payos/webhook", body);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("webhook_payload_too_large", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, host.Gateway.VerifyCalls);
        Assert.Empty(host.Evidence.Receipts);
    }

    [Theory]
    [InlineData(65536, false, 200)]
    [InlineData(65537, false, 413)]
    [InlineData(65537, true, 413)]
    public async Task BoundedBody_KnownOrChunkedLength_RejectsBeforeVerification(int bytes, bool chunked, int status)
    {
        using var host = new Host();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/payos/webhook")
        { Content = chunked ? new UnknownLengthContent(new string(' ', bytes)) : new StringContent(new string(' ', bytes), Encoding.UTF8, "application/json") };
        var response = await host.Client.SendAsync(request);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(status == 200 ? 1 : 0, host.Gateway.VerifyCalls);
        Assert.Equal(status == 200 ? 1 : 0, host.Evidence.Receipts.Count);
        if (status == 413) Assert.Contains("webhook_payload_too_large", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ExactUtf8BodyOnly_NoHeadersOrCredentialsInEvidenceOrResponse()
    {
        using var host = new Host();
        const string body = "\uFEFF {\r\n\"description\":\"Bến Thành\"} ";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/payos/webhook")
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("Authorization", "Bearer synthetic-header-only");
        request.Headers.Add("Cookie", "synthetic-cookie-only");
        var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(body, Assert.Single(host.Evidence.Receipts).RawPayload);
        Assert.Equal(body, host.Gateway.VerifiedBody);
        var output = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("RawPayload", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("synthetic-header-only", output);
        Assert.DoesNotContain("synthetic-cookie-only", output);
    }

    [Fact]
    public async Task InvalidSignature_Http400_ZeroEvidence()
    {
        using var host = new Host();
        host.Gateway.Verification = PaymentWebhookVerificationResult.Invalid();
        var response = await host.Client.PostAsync("/api/payments/payos/webhook", new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Evidence.Receipts);
        Assert.Empty(host.Settlement.Contexts);
    }

    [Fact]
    public async Task InvalidUtf8_IsRejectedWithoutVerification()
    {
        using var host = new Host();
        using var content = new ByteArrayContent([0xff]);
        content.Headers.ContentType = new("application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PostAsync("/api/payments/payos/webhook", content)).StatusCode);
        Assert.Equal(0, host.Gateway.VerifyCalls);
        Assert.Empty(host.Evidence.Receipts);
    }

    private sealed class Host : IDisposable
    {
        public PaymentEvidenceTestGateway Gateway { get; } = new() { Verification = PaymentWebhookVerificationResult.Valid(new(123, 19000, true)) };
        public PaymentEvidenceTestRepository Evidence { get; } = new();
        public PaymentEvidenceTestSettlement Settlement { get; } = new();
        private readonly IHost server;
        public HttpClient Client { get; }
        public Host(bool kestrel = false)
        {
            server = new HostBuilder().ConfigureWebHost(web =>
            {
                if (kestrel) web.UseKestrel().UseUrls("http://127.0.0.1:0");
                else web.UseTestServer();
                web.ConfigureServices(s =>
                {
                s.AddProblemDetails();
                s.AddExceptionHandler<GlobalExceptionHandler>();
                s.AddControllers().AddApplicationPart(typeof(PaymentsController).Assembly);
                s.AddSingleton<IPaymentWebhookService>(new PaymentWebhookService(Gateway, Settlement, Evidence,
                    Options.Create(new PaymentEvidenceOptions()), TimeProvider.System, NullLogger<PaymentWebhookService>.Instance));
                }).Configure(a => { a.UseExceptionHandler(); a.UseRouting(); a.UseEndpoints(e => e.MapControllers()); });
            }).Start();
            Client = kestrel ? new HttpClient { BaseAddress = new Uri(server.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single()) } : server.GetTestClient();
        }
        public void Dispose() { Client.Dispose(); server.Dispose(); }
    }

    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly string body;
        public UnknownLengthContent(string body) { this.body = body; Headers.ContentType = new("application/json"); }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(Encoding.UTF8.GetBytes(body)).AsTask();
    }
}
