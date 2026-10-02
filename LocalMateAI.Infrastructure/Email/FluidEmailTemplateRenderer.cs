using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Fluid;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Infrastructure.Email;

public sealed class FluidEmailTemplateRenderer : IEmailTemplateRenderer
{
    // Khớp LogicalName khai báo trong LocalMateAI.Infrastructure.csproj.
    private const string ResourcePrefix = "EmailTemplates.";
    private const string TemplateExtension = ".liquid";

    private static readonly FluidParser Parser = new();

    // Giữ nguyên chữ tiếng Việt, chỉ mã hoá ký tự HTML nguy hiểm (<, >, &, ").
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    // Cho template đọc mọi thuộc tính của model. An toàn vì template do team viết, không phải người dùng nhập.
    private static readonly TemplateOptions Options = new()
    {
        MemberAccessStrategy = UnsafeMemberAccessStrategy.Instance
    };

    // Mỗi template chỉ parse 1 lần, dùng lại cho mọi lần gửi.
    private readonly ConcurrentDictionary<string, IFluidTemplate> templates = new();

    public async Task<string> RenderAsync(
        string templateName,
        object model,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var template = templates.GetOrAdd(templateName, LoadTemplate);
        var context = new TemplateContext(model, Options);

        return await template.RenderAsync(context, Encoder);
    }

    private static IFluidTemplate LoadTemplate(string templateName)
    {
        var resourceName = ResourcePrefix + templateName + TemplateExtension;

        using var stream = typeof(FluidEmailTemplateRenderer).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Email template '{templateName}' was not found.");
        using var reader = new StreamReader(stream);
        var source = reader.ReadToEnd();

        return Parser.TryParse(source, out var template, out var error)
            ? template
            : throw new InvalidOperationException($"Email template '{templateName}' is invalid: {error}");
    }
}
