using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Authentication.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRenewableSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RenewableSessionFamilies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    RevocationReason = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RenewableSessionFamilies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefreshCredentials",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    FamilyId = table.Column<string>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "BLOB", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReplacedByTokenId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshCredentials_RefreshCredentials_ReplacedByTokenId",
                        column: x => x.ReplacedByTokenId,
                        principalTable: "RefreshCredentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RefreshCredentials_RenewableSessionFamilies_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "RenewableSessionFamilies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshCredentials_FamilyId",
                table: "RefreshCredentials",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshCredentials_ReplacedByTokenId",
                table: "RefreshCredentials",
                column: "ReplacedByTokenId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshCredentials_TokenHash",
                table: "RefreshCredentials",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RenewableSessionFamilies_UserId_RevokedAtUtc",
                table: "RenewableSessionFamilies",
                columns: new[] { "UserId", "RevokedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RefreshCredentials");

            migrationBuilder.DropTable(
                name: "RenewableSessionFamilies");
        }
    }
}
