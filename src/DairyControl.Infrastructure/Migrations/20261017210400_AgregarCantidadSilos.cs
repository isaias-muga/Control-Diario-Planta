using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DairyControl.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarCantidadSilos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CantidadSilos",
                table: "ConfiguracionesCalidad",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.UpdateData(
                table: "ConfiguracionesCalidad",
                keyColumn: "Id",
                keyValue: new Guid("5b0c6f0e-3f6a-4f7e-9a53-0d1c2b7a4e11"),
                column: "CantidadSilos",
                value: 3);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CantidadSilos",
                table: "ConfiguracionesCalidad");
        }
    }
}
