using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniBank.Api.Migrations
{
    /// <inheritdoc />
    public partial class TransferReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RequestKey",
                table: "Transfers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestedById",
                table: "Transfers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewReason",
                table: "Transfers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "Transfers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedById",
                table: "Transfers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_RequestedById_RequestKey",
                table: "Transfers",
                columns: new[] { "RequestedById", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_ReviewedById",
                table: "Transfers",
                column: "ReviewedById");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_Status_CreatedAt",
                table: "Transfers",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Transfers_Users_RequestedById",
                table: "Transfers",
                column: "RequestedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Transfers_Users_ReviewedById",
                table: "Transfers",
                column: "ReviewedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transfers_Users_RequestedById",
                table: "Transfers");

            migrationBuilder.DropForeignKey(
                name: "FK_Transfers_Users_ReviewedById",
                table: "Transfers");

            migrationBuilder.DropIndex(
                name: "IX_Transfers_RequestedById_RequestKey",
                table: "Transfers");

            migrationBuilder.DropIndex(
                name: "IX_Transfers_ReviewedById",
                table: "Transfers");

            migrationBuilder.DropIndex(
                name: "IX_Transfers_Status_CreatedAt",
                table: "Transfers");

            migrationBuilder.DropColumn(
                name: "RequestKey",
                table: "Transfers");

            migrationBuilder.DropColumn(
                name: "RequestedById",
                table: "Transfers");

            migrationBuilder.DropColumn(
                name: "ReviewReason",
                table: "Transfers");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "Transfers");

            migrationBuilder.DropColumn(
                name: "ReviewedById",
                table: "Transfers");
        }
    }
}
