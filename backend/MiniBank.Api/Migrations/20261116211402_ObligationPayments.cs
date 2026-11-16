using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniBank.Api.Migrations
{
    /// <inheritdoc />
    public partial class ObligationPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ObligationId",
                table: "Transfers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_ObligationId",
                table: "Transfers",
                column: "ObligationId",
                unique: true,
                filter: "\"ObligationId\" IS NOT NULL AND \"Status\" IN ('PENDING_APPROVAL', 'EXECUTED')");

            migrationBuilder.AddForeignKey(
                name: "FK_Transfers_Obligations_ObligationId",
                table: "Transfers",
                column: "ObligationId",
                principalTable: "Obligations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transfers_Obligations_ObligationId",
                table: "Transfers");

            migrationBuilder.DropIndex(
                name: "IX_Transfers_ObligationId",
                table: "Transfers");

            migrationBuilder.DropColumn(
                name: "ObligationId",
                table: "Transfers");
        }
    }
}
