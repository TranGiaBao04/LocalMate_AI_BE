using LocalMateAI.Application.DTOs.Email;
using LocalMateAI.Infrastructure.Email;

namespace LocalMateAI.Tests;

public sealed class FluidEmailTemplateRendererTests
{
    [Theory]
    [InlineData(EmailTemplateNames.OtpRegistration, "hoàn tất đăng ký")]
    [InlineData(EmailTemplateNames.OtpPasswordReset, "đặt mật khẩu mới")]
    public async Task Render_OtpTemplates_FillCodeAndMinutes(string templateName, string expectedText)
    {
        var html = await new FluidEmailTemplateRenderer().RenderAsync(templateName, new OtpEmailModel("482913", 10));

        Assert.Contains("482913", html);
        Assert.Contains("10 phút", html);
        Assert.Contains(expectedText, html);
        Assert.DoesNotContain("{{", html);
    }

    [Fact]
    public async Task Render_ValueWithHtml_IsEncoded()
    {
        var html = await new FluidEmailTemplateRenderer().RenderAsync(
            EmailTemplateNames.OtpRegistration,
            new OtpEmailModel("<b>1</b>", 10));

        Assert.Contains("&lt;b&gt;1&lt;/b&gt;", html);
        Assert.DoesNotContain("<b>1</b>", html);
    }

    [Fact]
    public async Task Render_SameTemplateTwice_ReturnsFreshValues()
    {
        var renderer = new FluidEmailTemplateRenderer();

        var first = await renderer.RenderAsync(EmailTemplateNames.OtpRegistration, new OtpEmailModel("111111", 10));
        var second = await renderer.RenderAsync(EmailTemplateNames.OtpRegistration, new OtpEmailModel("222222", 10));

        Assert.Contains("111111", first);
        Assert.Contains("222222", second);
        Assert.DoesNotContain("111111", second);
    }

    [Fact]
    public async Task Render_UnknownTemplate_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new FluidEmailTemplateRenderer().RenderAsync("does-not-exist", new OtpEmailModel("1", 1)));
    }
}
