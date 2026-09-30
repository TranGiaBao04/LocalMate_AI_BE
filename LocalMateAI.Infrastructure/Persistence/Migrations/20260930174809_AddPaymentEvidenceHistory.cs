using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalMateAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentEvidenceHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentWebhookReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderOrderCode = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,0)", nullable: false),
                    IsSuccessful = table.Column<bool>(type: "boolean", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawPayload = table.Column<string>(type: "text", nullable: true),
                    RawPayloadSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RawPayloadRetainUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawPayloadPurgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentWebhookReceipts", x => x.Id);
                    table.CheckConstraint("CK_WebhookReceipt_Hash", "\"RawPayloadSha256\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_WebhookReceipt_RawSize", "octet_length(\"RawPayload\") <= 65536");
                    table.CheckConstraint("CK_WebhookReceipt_RawState", "(\"RawPayload\" IS NOT NULL AND \"RawPayloadPurgedAt\" IS NULL) OR (\"RawPayload\" IS NULL AND \"RawPayloadPurgedAt\" IS NOT NULL AND \"RawPayloadPurgedAt\" >= \"RawPayloadRetainUntil\")");
                    table.CheckConstraint("CK_WebhookReceipt_Retention", "\"RawPayloadRetainUntil\" > \"ReceivedAt\"");
                    table.ForeignKey(
                        name: "FK_PaymentWebhookReceipts_PaymentOrders_PaymentOrderId",
                        column: x => x.PaymentOrderId,
                        principalTable: "PaymentOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentOrderStatusHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    WebhookReceiptId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentOrderStatusHistories", x => x.Id);
                    table.CheckConstraint("CK_PaymentHistory_RealTransition", "\"FromStatus\" IS NULL OR \"FromStatus\" <> \"ToStatus\"");
                    table.ForeignKey(
                        name: "FK_PaymentOrderStatusHistories_PaymentOrders_PaymentOrderId",
                        column: x => x.PaymentOrderId,
                        principalTable: "PaymentOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentOrderStatusHistories_PaymentWebhookReceipts_WebhookR~",
                        column: x => x.WebhookReceiptId,
                        principalTable: "PaymentWebhookReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentOrderStatusHistories_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrderStatusHistories_ActorUserId",
                table: "PaymentOrderStatusHistories",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrderStatusHistories_PaymentOrderId_OccurredAt",
                table: "PaymentOrderStatusHistories",
                columns: new[] { "PaymentOrderId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrderStatusHistories_WebhookReceiptId",
                table: "PaymentOrderStatusHistories",
                column: "WebhookReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookReceipts_PaymentOrderId",
                table: "PaymentWebhookReceipts",
                column: "PaymentOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookReceipts_ProviderOrderCode_ReceivedAt",
                table: "PaymentWebhookReceipts",
                columns: new[] { "ProviderOrderCode", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookReceipts_RawPayloadRetainUntil",
                table: "PaymentWebhookReceipts",
                column: "RawPayloadRetainUntil",
                filter: "\"RawPayload\" IS NOT NULL");

            migrationBuilder.Sql("""
                CREATE FUNCTION lm_protect_payment_history() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Payment status history is insert-only' USING ERRCODE='23514';
                END $$;
                CREATE TRIGGER protect_payment_history BEFORE UPDATE OR DELETE ON "PaymentOrderStatusHistories"
                FOR EACH ROW EXECUTE FUNCTION lm_protect_payment_history();
                CREATE TRIGGER protect_payment_history_truncate BEFORE TRUNCATE ON "PaymentOrderStatusHistories"
                FOR EACH STATEMENT EXECUTE FUNCTION lm_protect_payment_history();

                CREATE FUNCTION lm_protect_webhook_receipt() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP <> 'UPDATE' THEN
                        RAISE EXCEPTION 'Webhook receipts cannot be deleted or truncated' USING ERRCODE='23514';
                    END IF;
                    IF ROW(NEW."Id", NEW."PaymentOrderId", NEW."ProviderOrderCode", NEW."Amount",
                           NEW."IsSuccessful", NEW."ReceivedAt", NEW."RawPayloadSha256", NEW."RawPayloadRetainUntil")
                       IS DISTINCT FROM
                       ROW(OLD."Id", OLD."PaymentOrderId", OLD."ProviderOrderCode", OLD."Amount",
                           OLD."IsSuccessful", OLD."ReceivedAt", OLD."RawPayloadSha256", OLD."RawPayloadRetainUntil")
                       OR OLD."RawPayload" IS NULL OR OLD."RawPayloadPurgedAt" IS NOT NULL
                       OR NEW."RawPayload" IS NOT NULL OR NEW."RawPayloadPurgedAt" IS NULL
                       OR clock_timestamp() < OLD."RawPayloadRetainUntil"
                       OR NEW."RawPayloadPurgedAt" < OLD."RawPayloadRetainUntil" THEN
                        RAISE EXCEPTION 'Webhook metadata is immutable; only expired raw payload can be purged' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER protect_webhook_receipt BEFORE UPDATE OR DELETE ON "PaymentWebhookReceipts"
                FOR EACH ROW EXECUTE FUNCTION lm_protect_webhook_receipt();
                CREATE TRIGGER protect_webhook_receipt_truncate BEFORE TRUNCATE ON "PaymentWebhookReceipts"
                FOR EACH STATEMENT EXECUTE FUNCTION lm_protect_webhook_receipt();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "PaymentOrderStatusHistories")
                       OR EXISTS (SELECT 1 FROM "PaymentWebhookReceipts") THEN
                        RAISE EXCEPTION 'Unsafe downgrade: retained payment evidence exists' USING ERRCODE='23514';
                    END IF;
                END $$;
                DROP TRIGGER protect_payment_history ON "PaymentOrderStatusHistories";
                DROP TRIGGER protect_payment_history_truncate ON "PaymentOrderStatusHistories";
                DROP FUNCTION lm_protect_payment_history();
                DROP TRIGGER protect_webhook_receipt ON "PaymentWebhookReceipts";
                DROP TRIGGER protect_webhook_receipt_truncate ON "PaymentWebhookReceipts";
                DROP FUNCTION lm_protect_webhook_receipt();
                """);
            migrationBuilder.DropTable(
                name: "PaymentOrderStatusHistories");

            migrationBuilder.DropTable(
                name: "PaymentWebhookReceipts");
        }
    }
}
