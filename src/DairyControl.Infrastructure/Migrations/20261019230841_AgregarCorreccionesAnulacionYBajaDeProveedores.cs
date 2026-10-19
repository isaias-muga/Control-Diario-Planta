using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DairyControl.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarCorreccionesAnulacionYBajaDeProveedores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AnuladaEn",
                table: "Recepciones",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AnuladaPorUsuarioId",
                table: "Recepciones",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoAnulacion",
                table: "Recepciones",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Recepciones",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Activo",
                table: "Proveedores",
                type: "bit",
                nullable: false,
                // Every proveedor that exists is active; the default also keeps the previous release working (it inserts without this column)
                defaultValue: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Proveedores",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HistorialCambios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Entidad = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EntidadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CorreccionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Campo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ValorAnterior = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ValorNuevo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UsuarioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistorialCambios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HistorialCambios_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Recepciones_AnuladaPorUsuarioId",
                table: "Recepciones",
                column: "AnuladaPorUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_HistorialCambios_Entidad_EntidadId_EnUtc",
                table: "HistorialCambios",
                columns: new[] { "Entidad", "EntidadId", "EnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HistorialCambios_UsuarioId",
                table: "HistorialCambios",
                column: "UsuarioId");

            migrationBuilder.AddForeignKey(
                name: "FK_Recepciones_Usuarios_AnuladaPorUsuarioId",
                table: "Recepciones",
                column: "AnuladaPorUsuarioId",
                principalTable: "Usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Recepciones_Usuarios_AnuladaPorUsuarioId",
                table: "Recepciones");

            migrationBuilder.DropTable(
                name: "HistorialCambios");

            migrationBuilder.DropIndex(
                name: "IX_Recepciones_AnuladaPorUsuarioId",
                table: "Recepciones");

            migrationBuilder.DropColumn(
                name: "AnuladaEn",
                table: "Recepciones");

            migrationBuilder.DropColumn(
                name: "AnuladaPorUsuarioId",
                table: "Recepciones");

            migrationBuilder.DropColumn(
                name: "MotivoAnulacion",
                table: "Recepciones");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Recepciones");

            migrationBuilder.DropColumn(
                name: "Activo",
                table: "Proveedores");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Proveedores");
        }
    }
}
