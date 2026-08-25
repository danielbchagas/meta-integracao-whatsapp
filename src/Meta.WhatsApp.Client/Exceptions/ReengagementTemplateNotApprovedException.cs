namespace Meta.WhatsApp.Exceptions;

public sealed class ReengagementTemplateNotApprovedException : InvalidOperationException
{
    public ReengagementTemplateNotApprovedException(string templateName, string languageCode, string? status)
        : base($"Template '{templateName}' ({languageCode}) cannot be used for reengagement because its status is '{status ?? "NOT_FOUND"}', not 'APPROVED'.")
    {
        TemplateName = templateName;
        LanguageCode = languageCode;
        Status = status;
    }

    public string TemplateName { get; }

    public string LanguageCode { get; }

    public string? Status { get; }
}
