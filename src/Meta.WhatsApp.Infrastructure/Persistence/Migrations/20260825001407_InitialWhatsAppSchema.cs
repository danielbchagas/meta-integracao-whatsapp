using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814, CA1861 // Generated migration data uses constant arrays.

namespace Meta.WhatsApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialWhatsAppSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "integration_inbox",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    message_type = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    payload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_error = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_inbox", x => x.MessageId);
                });

            migrationBuilder.CreateTable(
                name: "integration_outbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    aggregate_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    aggregate_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    event_type = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    causation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    claimed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    claimed_by = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    last_error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_message_definition",
                columns: table => new
                {
                    code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    content_strategy = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    template_name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    language = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    renderer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    enabled = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_whatsapp_message_definition", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_waba",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    meta_waba_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    business_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    credential_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    last_sync_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_whatsapp_waba", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_operation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WabaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    operation_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    attempts = table.Column<int>(type: "int", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_whatsapp_operation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_whatsapp_operation_whatsapp_waba_WabaId",
                        column: x => x.WabaId,
                        principalTable: "whatsapp_waba",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_phone_number",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WabaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    meta_phone_number_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    display_phone_number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    verified_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    quality_rating = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    platform_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_whatsapp_phone_number", x => x.Id);
                    table.ForeignKey(
                        name: "FK_whatsapp_phone_number_whatsapp_waba_WabaId",
                        column: x => x.WabaId,
                        principalTable: "whatsapp_waba",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_template",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WabaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    meta_template_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    language = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    category = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    current_version = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_whatsapp_template", x => x.Id);
                    table.ForeignKey(
                        name: "FK_whatsapp_template_whatsapp_waba_WabaId",
                        column: x => x.WabaId,
                        principalTable: "whatsapp_waba",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_message",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    WabaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PhoneNumberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    recipient = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    message_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    content_strategy = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    payload_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    template_name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    meta_message_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    correlation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    read_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_whatsapp_message", x => x.Id);
                    table.ForeignKey(
                        name: "FK_whatsapp_message_whatsapp_phone_number_PhoneNumberId",
                        column: x => x.PhoneNumberId,
                        principalTable: "whatsapp_phone_number",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_whatsapp_message_whatsapp_waba_WabaId",
                        column: x => x.WabaId,
                        principalTable: "whatsapp_waba",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_template_version",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    components_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_whatsapp_template_version", x => x.Id);
                    table.ForeignKey(
                        name: "FK_whatsapp_template_version_whatsapp_template_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "whatsapp_template",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_rendered_media",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    storage_path = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    mime_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    width = table.Column<int>(type: "int", nullable: false),
                    height = table.Column<int>(type: "int", nullable: false),
                    renderer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    renderer_version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    meta_media_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_whatsapp_rendered_media", x => x.Id);
                    table.ForeignKey(
                        name: "FK_whatsapp_rendered_media_whatsapp_message_MessageId",
                        column: x => x.MessageId,
                        principalTable: "whatsapp_message",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "whatsapp_message_definition",
                columns: new[] { "code", "content_strategy", "created_at", "enabled", "language", "renderer", "template_name", "updated_at" },
                values: new object[,]
                {
                    { "AVISO_SIMPLES", "FreeText", new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "pt_BR", null, null, new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "PROPOSTA_APROVADA", "TextTemplate", new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "pt_BR", null, "proposta_aprovada", new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "RESUMO_PROPOSTAS", "ImageTemplate", new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "pt_BR", "proposal-summary", "resumo_propostas", new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "RESUMO_VEICULOS", "ImageTemplate", new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "pt_BR", "vehicle-summary", "resumo_veiculos", new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_integration_inbox_processed_at_received_at",
                table: "integration_inbox",
                columns: new[] { "processed_at", "received_at" });

            migrationBuilder.CreateIndex(
                name: "IX_integration_outbox_claimed_at_claimed_by",
                table: "integration_outbox",
                columns: new[] { "claimed_at", "claimed_by" });

            migrationBuilder.CreateIndex(
                name: "IX_integration_outbox_published_at_next_attempt_at",
                table: "integration_outbox",
                columns: new[] { "published_at", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "IX_whatsapp_message_idempotency_key",
                table: "whatsapp_message",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_whatsapp_message_meta_message_id",
                table: "whatsapp_message",
                column: "meta_message_id",
                unique: true,
                filter: "[meta_message_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_whatsapp_message_PhoneNumberId",
                table: "whatsapp_message",
                column: "PhoneNumberId");

            migrationBuilder.CreateIndex(
                name: "IX_whatsapp_message_status_created_at",
                table: "whatsapp_message",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_whatsapp_message_WabaId",
                table: "whatsapp_message",
                column: "WabaId");

            migrationBuilder.CreateIndex(
                name: "IX_whatsapp_operation_status_next_attempt_at",
                table: "whatsapp_operation",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "IX_whatsapp_operation_WabaId",
                table: "whatsapp_operation",
                column: "WabaId");

            migrationBuilder.CreateIndex(
                name: "IX_whatsapp_phone_number_meta_phone_number_id",
                table: "whatsapp_phone_number",
                column: "meta_phone_number_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_whatsapp_phone_number_WabaId",
                table: "whatsapp_phone_number",
                column: "WabaId");

            migrationBuilder.CreateIndex(
                name: "IX_whatsapp_rendered_media_MessageId",
                table: "whatsapp_rendered_media",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_whatsapp_rendered_media_sha256_renderer_renderer_version",
                table: "whatsapp_rendered_media",
                columns: new[] { "sha256", "renderer", "renderer_version" });

            migrationBuilder.CreateIndex(
                name: "IX_whatsapp_template_meta_template_id",
                table: "whatsapp_template",
                column: "meta_template_id",
                unique: true,
                filter: "[meta_template_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_whatsapp_template_WabaId_name_language",
                table: "whatsapp_template",
                columns: new[] { "WabaId", "name", "language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_whatsapp_template_version_TemplateId_version",
                table: "whatsapp_template_version",
                columns: new[] { "TemplateId", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_whatsapp_waba_meta_waba_id",
                table: "whatsapp_waba",
                column: "meta_waba_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_inbox");

            migrationBuilder.DropTable(
                name: "integration_outbox");

            migrationBuilder.DropTable(
                name: "whatsapp_message_definition");

            migrationBuilder.DropTable(
                name: "whatsapp_operation");

            migrationBuilder.DropTable(
                name: "whatsapp_rendered_media");

            migrationBuilder.DropTable(
                name: "whatsapp_template_version");

            migrationBuilder.DropTable(
                name: "whatsapp_message");

            migrationBuilder.DropTable(
                name: "whatsapp_template");

            migrationBuilder.DropTable(
                name: "whatsapp_phone_number");

            migrationBuilder.DropTable(
                name: "whatsapp_waba");
        }
    }
}
