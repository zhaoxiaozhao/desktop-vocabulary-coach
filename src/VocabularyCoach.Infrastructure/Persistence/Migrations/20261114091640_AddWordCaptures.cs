using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VocabularyCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWordCaptures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WordCaptures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WordId = table.Column<int>(type: "INTEGER", nullable: false),
                    RawText = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    NormalizedWord = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CapturedAt = table.Column<string>(type: "TEXT", nullable: false),
                    CaptureCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WordCaptures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WordCaptures_Words_WordId",
                        column: x => x.WordId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WordCaptures_CapturedAt",
                table: "WordCaptures",
                column: "CapturedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WordCaptures_NormalizedWord",
                table: "WordCaptures",
                column: "NormalizedWord",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WordCaptures_WordId",
                table: "WordCaptures",
                column: "WordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WordCaptures");
        }
    }
}
