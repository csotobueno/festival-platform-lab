using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Festival.Modules.Registration.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialRegistration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "registration");

            migrationBuilder.CreateTable(
                name: "registrations",
                schema: "registration",
                columns: table => new
                {
                    registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registrations", x => x.registration_id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_registrations_email",
                schema: "registration",
                table: "registrations",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "registrations",
                schema: "registration");
        }
    }
}
