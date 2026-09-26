namespace LocalMateAI.Application.Interfaces.Services;

public interface IEmailTemplateRenderer
{
    // Dựng nội dung HTML từ template tên templateName (xem EmailTemplateNames) với dữ liệu model.
    Task<string> RenderAsync(
        string templateName,
        object model,
        CancellationToken cancellationToken = default);
}
