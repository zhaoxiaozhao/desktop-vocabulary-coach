using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VocabularyCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdaptiveDifficulty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "PreferredNewWordRatio",
                table: "UserLearningProfile",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreferredNewWordRatio",
                table: "UserLearningProfile");
        }
    }
}
