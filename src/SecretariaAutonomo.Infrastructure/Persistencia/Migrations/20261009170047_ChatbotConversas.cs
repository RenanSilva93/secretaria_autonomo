using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecretariaAutonomo.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class ChatbotConversas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConversasWhatsApp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Estado = table.Column<string>(type: "jsonb", nullable: true),
                    AtualizadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversasWhatsApp", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversasWhatsApp_Telefone",
                table: "ConversasWhatsApp",
                column: "Telefone",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversasWhatsApp");
        }
    }
}
