using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecretariaAutonomo.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class Financeiro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Lancamentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    Categoria = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    FormaPagamento = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Pago = table.Column<bool>(type: "boolean", nullable: false),
                    AgendamentoId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lancamentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Lancamentos_Agendamentos_AgendamentoId",
                        column: x => x.AgendamentoId,
                        principalTable: "Agendamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Lancamentos_AgendamentoId",
                table: "Lancamentos",
                column: "AgendamentoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lancamentos_TenantId_Data",
                table: "Lancamentos",
                columns: new[] { "TenantId", "Data" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Lancamentos");
        }
    }
}
