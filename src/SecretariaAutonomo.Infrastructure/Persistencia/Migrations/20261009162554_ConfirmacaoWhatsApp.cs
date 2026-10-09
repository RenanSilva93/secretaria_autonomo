using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecretariaAutonomo.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class ConfirmacaoWhatsApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConfirmacaoEnviadaEm",
                table: "Agendamentos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Agendamentos_ConfirmacaoEnviadaEm_CriadoEm",
                table: "Agendamentos",
                columns: new[] { "ConfirmacaoEnviadaEm", "CriadoEm" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Agendamentos_ConfirmacaoEnviadaEm_CriadoEm",
                table: "Agendamentos");

            migrationBuilder.DropColumn(
                name: "ConfirmacaoEnviadaEm",
                table: "Agendamentos");
        }
    }
}
