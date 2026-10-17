using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DairyControl.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarUsuariosOperariosYAuditoria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OperarioId",
                table: "Recepciones",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RegistradoEn",
                table: "Recepciones",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RegistradoPorUsuarioId",
                table: "Recepciones",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreadoPorUsuarioId",
                table: "Proveedores",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Operarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    PinHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Operarios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Rol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SecurityStamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DebeCambiarPassword = table.Column<bool>(type: "bit", nullable: false),
                    FallosLogin = table.Column<int>(type: "int", nullable: false),
                    LoginBloqueadoHastaUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FallosPin = table.Column<int>(type: "int", nullable: false),
                    PrimerFalloPinUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PinBloqueadoHastaUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Recepciones_OperarioId",
                table: "Recepciones",
                column: "OperarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Recepciones_RegistradoPorUsuarioId",
                table: "Recepciones",
                column: "RegistradoPorUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Proveedores_CreadoPorUsuarioId",
                table: "Proveedores",
                column: "CreadoPorUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Operarios_PinHash",
                table: "Operarios",
                column: "PinHash",
                unique: true,
                filter: "[Activo] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_Username",
                table: "Usuarios",
                column: "Username",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Proveedores_Usuarios_CreadoPorUsuarioId",
                table: "Proveedores",
                column: "CreadoPorUsuarioId",
                principalTable: "Usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Recepciones_Operarios_OperarioId",
                table: "Recepciones",
                column: "OperarioId",
                principalTable: "Operarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Recepciones_Usuarios_RegistradoPorUsuarioId",
                table: "Recepciones",
                column: "RegistradoPorUsuarioId",
                principalTable: "Usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Proveedores_Usuarios_CreadoPorUsuarioId",
                table: "Proveedores");

            migrationBuilder.DropForeignKey(
                name: "FK_Recepciones_Operarios_OperarioId",
                table: "Recepciones");

            migrationBuilder.DropForeignKey(
                name: "FK_Recepciones_Usuarios_RegistradoPorUsuarioId",
                table: "Recepciones");

            migrationBuilder.DropTable(
                name: "Operarios");

            migrationBuilder.DropTable(
                name: "Usuarios");

            migrationBuilder.DropIndex(
                name: "IX_Recepciones_OperarioId",
                table: "Recepciones");

            migrationBuilder.DropIndex(
                name: "IX_Recepciones_RegistradoPorUsuarioId",
                table: "Recepciones");

            migrationBuilder.DropIndex(
                name: "IX_Proveedores_CreadoPorUsuarioId",
                table: "Proveedores");

            migrationBuilder.DropColumn(
                name: "OperarioId",
                table: "Recepciones");

            migrationBuilder.DropColumn(
                name: "RegistradoEn",
                table: "Recepciones");

            migrationBuilder.DropColumn(
                name: "RegistradoPorUsuarioId",
                table: "Recepciones");

            migrationBuilder.DropColumn(
                name: "CreadoPorUsuarioId",
                table: "Proveedores");
        }
    }
}
