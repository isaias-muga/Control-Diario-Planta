using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DairyControl.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarCuentasProtegidas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Protegido",
                table: "Usuarios",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Protegido",
                table: "Usuarios");
        }
    }
}
