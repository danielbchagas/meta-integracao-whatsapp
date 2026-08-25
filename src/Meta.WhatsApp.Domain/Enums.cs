namespace Meta.WhatsApp.Domain;

public enum WabaStatus
{
    Pending,
    Active,
    InvalidCredentials,
    Disabled,
    Failed
}

public enum ContentStrategy
{
    FreeText,
    TextTemplate,
    ImageTemplate
}

public enum WhatsAppMessageStatus
{
    Created,
    Rendering,
    Rendered,
    MediaUploading,
    MediaReady,
    Queued,
    Sending,
    Sent,
    Delivered,
    Read,
    RenderFailed,
    MediaUploadFailed,
    SendFailed,
    FailedPermanent
}

public enum OperationStatus
{
    Pending,
    Processing,
    WaitingMeta,
    Succeeded,
    Failed,
    FailedPermanent
}

public enum OperationType
{
    CreateTemplate,
    UpdateTemplate,
    DeleteTemplate,
    SendMessage,
    SyncWaba,
    SyncTemplate,
    UploadMedia
}
