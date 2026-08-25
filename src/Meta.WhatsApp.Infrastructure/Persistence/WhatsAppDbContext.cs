using Microsoft.EntityFrameworkCore;
using Meta.WhatsApp.Domain;

namespace Meta.WhatsApp.Infrastructure.Persistence;

public sealed class WhatsAppDbContext(DbContextOptions<WhatsAppDbContext> options) : DbContext(options)
{
    public DbSet<Waba> Wabas => Set<Waba>();

    public DbSet<PhoneNumber> PhoneNumbers => Set<PhoneNumber>();

    public DbSet<MessageTemplate> Templates => Set<MessageTemplate>();

    public DbSet<MessageTemplateVersion> TemplateVersions => Set<MessageTemplateVersion>();

    public DbSet<MessageDefinition> MessageDefinitions => Set<MessageDefinition>();

    public DbSet<WhatsAppMessage> Messages => Set<WhatsAppMessage>();

    public DbSet<RenderedMedia> RenderedMedia => Set<RenderedMedia>();

    public DbSet<IntegrationOperation> Operations => Set<IntegrationOperation>();

    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    public DbSet<InboxMessage> Inbox => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ConfigureWaba(modelBuilder);
        ConfigurePhoneNumber(modelBuilder);
        ConfigureTemplate(modelBuilder);
        ConfigureMessageDefinition(modelBuilder);
        ConfigureMessage(modelBuilder);
        ConfigureRenderedMedia(modelBuilder);
        ConfigureOperation(modelBuilder);
        ConfigureOutbox(modelBuilder);
        ConfigureInbox(modelBuilder);
    }

    private static void ConfigureWaba(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Waba>();
        entity.ToTable("whatsapp_waba");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Id).ValueGeneratedNever();
        entity.HasIndex(item => item.MetaWabaId).IsUnique();
        entity.Property(item => item.MetaWabaId).HasColumnName("meta_waba_id").HasMaxLength(100).IsRequired();
        entity.Property(item => item.BusinessId).HasColumnName("business_id").HasMaxLength(100);
        entity.Property(item => item.Name).HasColumnName("name").HasMaxLength(200);
        entity.Property(item => item.CredentialKey).HasColumnName("credential_key").HasMaxLength(200).IsRequired();
        entity.Property(item => item.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(40);
        entity.Property(item => item.LastSyncAt).HasColumnName("last_sync_at");
        entity.Property(item => item.CreatedAt).HasColumnName("created_at");
        entity.Property(item => item.UpdatedAt).HasColumnName("updated_at");
        entity.Property(item => item.RowVersion).HasColumnName("row_version").IsRowVersion();
    }

    private static void ConfigurePhoneNumber(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PhoneNumber>();
        entity.ToTable("whatsapp_phone_number");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Id).ValueGeneratedNever();
        entity.HasIndex(item => item.MetaPhoneNumberId).IsUnique();
        entity.HasIndex(item => item.WabaId);
        entity.HasOne<Waba>().WithMany().HasForeignKey(item => item.WabaId).OnDelete(DeleteBehavior.Cascade);
        entity.Property(item => item.MetaPhoneNumberId).HasColumnName("meta_phone_number_id").HasMaxLength(100);
        entity.Property(item => item.DisplayPhoneNumber).HasColumnName("display_phone_number").HasMaxLength(40);
        entity.Property(item => item.VerifiedName).HasColumnName("verified_name").HasMaxLength(200);
        entity.Property(item => item.QualityRating).HasColumnName("quality_rating").HasMaxLength(40);
        entity.Property(item => item.PlatformType).HasColumnName("platform_type").HasMaxLength(40);
        entity.Property(item => item.Status).HasColumnName("status").HasMaxLength(40);
        entity.Property(item => item.CreatedAt).HasColumnName("created_at");
        entity.Property(item => item.UpdatedAt).HasColumnName("updated_at");
        entity.Property(item => item.RowVersion).HasColumnName("row_version").IsRowVersion();
    }

    private static void ConfigureTemplate(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<MessageTemplate>();
        entity.ToTable("whatsapp_template");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Id).ValueGeneratedNever();
        entity.HasIndex(item => new { item.WabaId, item.Name, item.Language }).IsUnique();
        entity.HasIndex(item => item.MetaTemplateId).IsUnique().HasFilter("[meta_template_id] IS NOT NULL");
        entity.HasOne<Waba>().WithMany().HasForeignKey(item => item.WabaId).OnDelete(DeleteBehavior.Cascade);
        entity.Property(item => item.MetaTemplateId).HasColumnName("meta_template_id").HasMaxLength(100);
        entity.Property(item => item.Name).HasColumnName("name").HasMaxLength(512);
        entity.Property(item => item.Language).HasColumnName("language").HasMaxLength(20);
        entity.Property(item => item.Category).HasColumnName("category").HasMaxLength(40);
        entity.Property(item => item.Status).HasColumnName("status").HasMaxLength(40);
        entity.Property(item => item.CurrentVersion).HasColumnName("current_version");
        entity.Property(item => item.CreatedAt).HasColumnName("created_at");
        entity.Property(item => item.UpdatedAt).HasColumnName("updated_at");
        entity.Property(item => item.RowVersion).HasColumnName("row_version").IsRowVersion();
        entity.HasMany(item => item.Versions)
            .WithOne()
            .HasForeignKey(item => item.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.Navigation(item => item.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);

        var version = modelBuilder.Entity<MessageTemplateVersion>();
        version.ToTable("whatsapp_template_version");
        version.HasKey(item => item.Id);
        version.Property(item => item.Id).ValueGeneratedNever();
        version.HasIndex(item => new { item.TemplateId, item.Version }).IsUnique();
        version.Property(item => item.Version).HasColumnName("version");
        version.Property(item => item.ComponentsJson).HasColumnName("components_json").HasColumnType("nvarchar(max)");
        version.Property(item => item.Hash).HasColumnName("hash").HasMaxLength(64);
        version.Property(item => item.CreatedAt).HasColumnName("created_at");
    }

    private static void ConfigureMessageDefinition(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<MessageDefinition>();
        entity.ToTable("whatsapp_message_definition");
        entity.HasKey(item => item.Code);
        entity.Property(item => item.Code).HasColumnName("code").HasMaxLength(100);
        entity.Property(item => item.ContentStrategy).HasColumnName("content_strategy").HasConversion<string>().HasMaxLength(40);
        entity.Property(item => item.TemplateName).HasColumnName("template_name").HasMaxLength(512);
        entity.Property(item => item.Language).HasColumnName("language").HasMaxLength(20);
        entity.Property(item => item.Renderer).HasColumnName("renderer").HasMaxLength(200);
        entity.Property(item => item.Enabled).HasColumnName("enabled");
        entity.Property(item => item.CreatedAt).HasColumnName("created_at");
        entity.Property(item => item.UpdatedAt).HasColumnName("updated_at");
        entity.Property(item => item.RowVersion).HasColumnName("row_version").IsRowVersion();

        var seededAt = DateTimeOffset.UnixEpoch;
        entity.HasData(
            new
            {
                Code = "AVISO_SIMPLES",
                ContentStrategy = ContentStrategy.FreeText,
                TemplateName = (string?)null,
                Language = "pt_BR",
                Renderer = (string?)null,
                Enabled = true,
                CreatedAt = seededAt,
                UpdatedAt = seededAt
            },
            new
            {
                Code = "PROPOSTA_APROVADA",
                ContentStrategy = ContentStrategy.TextTemplate,
                TemplateName = "proposta_aprovada",
                Language = "pt_BR",
                Renderer = (string?)null,
                Enabled = true,
                CreatedAt = seededAt,
                UpdatedAt = seededAt
            },
            new
            {
                Code = "RESUMO_PROPOSTAS",
                ContentStrategy = ContentStrategy.ImageTemplate,
                TemplateName = "resumo_propostas",
                Language = "pt_BR",
                Renderer = "proposal-summary",
                Enabled = true,
                CreatedAt = seededAt,
                UpdatedAt = seededAt
            },
            new
            {
                Code = "RESUMO_VEICULOS",
                ContentStrategy = ContentStrategy.ImageTemplate,
                TemplateName = "resumo_veiculos",
                Language = "pt_BR",
                Renderer = "vehicle-summary",
                Enabled = false,
                CreatedAt = seededAt,
                UpdatedAt = seededAt
            });
    }

    private static void ConfigureMessage(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<WhatsAppMessage>();
        entity.ToTable("whatsapp_message");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Id).ValueGeneratedNever();
        entity.HasIndex(item => item.IdempotencyKey).IsUnique();
        entity.HasIndex(item => item.MetaMessageId).IsUnique().HasFilter("[meta_message_id] IS NOT NULL");
        entity.HasIndex(item => new { item.Status, item.CreatedAt });
        entity.HasOne<Waba>().WithMany().HasForeignKey(item => item.WabaId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne<PhoneNumber>().WithMany().HasForeignKey(item => item.PhoneNumberId).OnDelete(DeleteBehavior.NoAction);
        entity.Property(item => item.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200);
        entity.Property(item => item.Recipient).HasColumnName("recipient").HasMaxLength(20);
        entity.Property(item => item.MessageType).HasColumnName("message_type").HasMaxLength(100);
        entity.Property(item => item.ContentStrategy).HasColumnName("content_strategy").HasConversion<string>().HasMaxLength(40);
        entity.Property(item => item.PayloadJson).HasColumnName("payload_json").HasColumnType("nvarchar(max)");
        entity.Property(item => item.TemplateName).HasColumnName("template_name").HasMaxLength(512);
        entity.Property(item => item.MetaMessageId).HasColumnName("meta_message_id").HasMaxLength(200);
        entity.Property(item => item.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(40);
        entity.Property(item => item.CorrelationId).HasColumnName("correlation_id");
        entity.Property(item => item.CreatedAt).HasColumnName("created_at");
        entity.Property(item => item.UpdatedAt).HasColumnName("updated_at");
        entity.Property(item => item.SentAt).HasColumnName("sent_at");
        entity.Property(item => item.DeliveredAt).HasColumnName("delivered_at");
        entity.Property(item => item.ReadAt).HasColumnName("read_at");
        entity.Property(item => item.LastError).HasColumnName("last_error").HasColumnType("nvarchar(max)");
        entity.Property(item => item.RowVersion).HasColumnName("row_version").IsRowVersion();
    }

    private static void ConfigureRenderedMedia(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<RenderedMedia>();
        entity.ToTable("whatsapp_rendered_media");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Id).ValueGeneratedNever();
        entity.HasIndex(item => item.MessageId).IsUnique();
        entity.HasIndex(item => new { item.Sha256, item.Renderer, item.RendererVersion });
        entity.HasOne<WhatsAppMessage>().WithOne().HasForeignKey<RenderedMedia>(item => item.MessageId).OnDelete(DeleteBehavior.Cascade);
        entity.Property(item => item.StoragePath).HasColumnName("storage_path").HasMaxLength(1000);
        entity.Property(item => item.MimeType).HasColumnName("mime_type").HasMaxLength(100);
        entity.Property(item => item.Sha256).HasColumnName("sha256").HasMaxLength(64);
        entity.Property(item => item.Size).HasColumnName("size");
        entity.Property(item => item.Width).HasColumnName("width");
        entity.Property(item => item.Height).HasColumnName("height");
        entity.Property(item => item.Renderer).HasColumnName("renderer").HasMaxLength(200);
        entity.Property(item => item.RendererVersion).HasColumnName("renderer_version").HasMaxLength(50);
        entity.Property(item => item.MetaMediaId).HasColumnName("meta_media_id").HasMaxLength(200);
        entity.Property(item => item.CreatedAt).HasColumnName("created_at");
        entity.Property(item => item.ExpiresAt).HasColumnName("expires_at");
        entity.Property(item => item.RowVersion).HasColumnName("row_version").IsRowVersion();
    }

    private static void ConfigureOperation(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<IntegrationOperation>();
        entity.ToTable("whatsapp_operation");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Id).ValueGeneratedNever();
        entity.HasIndex(item => new { item.Status, item.NextAttemptAt });
        entity.HasOne<Waba>().WithMany().HasForeignKey(item => item.WabaId).OnDelete(DeleteBehavior.NoAction);
        entity.Property(item => item.OperationType).HasColumnName("operation_type").HasConversion<string>().HasMaxLength(50);
        entity.Property(item => item.EntityId).HasColumnName("entity_id");
        entity.Property(item => item.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(40);
        entity.Property(item => item.Attempts).HasColumnName("attempts");
        entity.Property(item => item.NextAttemptAt).HasColumnName("next_attempt_at");
        entity.Property(item => item.LastError).HasColumnName("last_error").HasColumnType("nvarchar(max)");
        entity.Property(item => item.CreatedAt).HasColumnName("created_at");
        entity.Property(item => item.CompletedAt).HasColumnName("completed_at");
        entity.Property(item => item.CorrelationId).HasColumnName("correlation_id");
        entity.Property(item => item.RowVersion).HasColumnName("row_version").IsRowVersion();
    }

    private static void ConfigureOutbox(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OutboxMessage>();
        entity.ToTable("integration_outbox");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Id).ValueGeneratedNever();
        entity.HasIndex(item => new { item.PublishedAt, item.NextAttemptAt });
        entity.HasIndex(item => new { item.ClaimedAt, item.ClaimedBy });
        entity.Property(item => item.AggregateType).HasColumnName("aggregate_type").HasMaxLength(100);
        entity.Property(item => item.AggregateId).HasColumnName("aggregate_id").HasMaxLength(200);
        entity.Property(item => item.EventType).HasColumnName("event_type").HasMaxLength(150);
        entity.Property(item => item.Payload).HasColumnName("payload").HasColumnType("nvarchar(max)");
        entity.Property(item => item.CorrelationId).HasColumnName("correlation_id");
        entity.Property(item => item.CausationId).HasColumnName("causation_id");
        entity.Property(item => item.OccurredAt).HasColumnName("occurred_at");
        entity.Property(item => item.PublishedAt).HasColumnName("published_at");
        entity.Property(item => item.AttemptCount).HasColumnName("attempt_count");
        entity.Property(item => item.NextAttemptAt).HasColumnName("next_attempt_at");
        entity.Property(item => item.ClaimedAt).HasColumnName("claimed_at");
        entity.Property(item => item.ClaimedBy).HasColumnName("claimed_by").HasMaxLength(200);
        entity.Property(item => item.LastError).HasColumnName("last_error").HasColumnType("nvarchar(max)");
        entity.Property(item => item.RowVersion).HasColumnName("row_version").IsRowVersion();
    }

    private static void ConfigureInbox(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<InboxMessage>();
        entity.ToTable("integration_inbox");
        entity.HasKey(item => item.MessageId);
        entity.Property(item => item.MessageId).ValueGeneratedNever();
        entity.HasIndex(item => new { item.ProcessedAt, item.ReceivedAt });
        entity.Property(item => item.MessageType).HasColumnName("message_type").HasMaxLength(150);
        entity.Property(item => item.Payload).HasColumnName("payload").HasColumnType("nvarchar(max)");
        entity.Property(item => item.ReceivedAt).HasColumnName("received_at");
        entity.Property(item => item.ProcessedAt).HasColumnName("processed_at");
        entity.Property(item => item.LastError).HasColumnName("last_error").HasColumnType("nvarchar(max)");
    }
}
