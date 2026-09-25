using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Store.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NarrowSearchIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_Name_Search",
                table: "Products");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Name_Search",
                table: "Products",
                column: "Name",
                filter: "[DeletedAt] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_Name_Search",
                table: "Products");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Name_Search",
                table: "Products",
                column: "Name",
                filter: "[DeletedAt] IS NULL")
                .Annotation("SqlServer:Include", new[] { "ShortDescription" });
        }
    }
}
