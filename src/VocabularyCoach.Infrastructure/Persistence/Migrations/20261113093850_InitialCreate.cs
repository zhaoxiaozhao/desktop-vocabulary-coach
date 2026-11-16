using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VocabularyCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "UserLearningProfile",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CurrentDifficulty = table.Column<int>(type: "INTEGER", nullable: false),
                    LevelEstimate = table.Column<double>(type: "REAL", nullable: true),
                    RecentSampleSize = table.Column<int>(type: "INTEGER", nullable: false),
                    RecentRecognized = table.Column<int>(type: "INTEGER", nullable: false),
                    RecentVague = table.Column<int>(type: "INTEGER", nullable: false),
                    RecentUnknown = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLearningProfile", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Words",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Word = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    NormalizedWord = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Phonetic = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Difficulty = table.Column<int>(type: "INTEGER", nullable: false),
                    FrequencyRank = table.Column<int>(type: "INTEGER", nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Words", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiGenerationCache",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WordId = table.Column<int>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiGenerationCache", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiGenerationCache_Words_WordId",
                        column: x => x.WordId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LearningSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WordId = table.Column<int>(type: "INTEGER", nullable: false),
                    ShownAt = table.Column<string>(type: "TEXT", nullable: false),
                    RespondedAt = table.Column<string>(type: "TEXT", nullable: true),
                    Action = table.Column<int>(type: "INTEGER", nullable: false),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: true),
                    Source = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearningSessions_Words_WordId",
                        column: x => x.WordId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReviewLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WordId = table.Column<int>(type: "INTEGER", nullable: false),
                    Rating = table.Column<int>(type: "INTEGER", nullable: false),
                    UserFeedback = table.Column<int>(type: "INTEGER", nullable: false),
                    ReviewedAt = table.Column<string>(type: "TEXT", nullable: false),
                    PreviousDueAt = table.Column<string>(type: "TEXT", nullable: true),
                    NextDueAt = table.Column<string>(type: "TEXT", nullable: true),
                    Source = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewLogs_Words_WordId",
                        column: x => x.WordId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserWordProgress",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WordId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Repetition = table.Column<int>(type: "INTEGER", nullable: false),
                    EaseFactor = table.Column<double>(type: "REAL", nullable: false),
                    IntervalDays = table.Column<double>(type: "REAL", nullable: false),
                    DueAt = table.Column<string>(type: "TEXT", nullable: true),
                    LastReviewedAt = table.Column<string>(type: "TEXT", nullable: true),
                    LapseCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserWordProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserWordProgress_Words_WordId",
                        column: x => x.WordId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WordDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WordId = table.Column<int>(type: "INTEGER", nullable: false),
                    PartOfSpeech = table.Column<string>(type: "TEXT", nullable: true),
                    DefinitionZh = table.Column<string>(type: "TEXT", nullable: false),
                    DefinitionEn = table.Column<string>(type: "TEXT", nullable: true),
                    ExampleEn = table.Column<string>(type: "TEXT", nullable: true),
                    ExampleZh = table.Column<string>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WordDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WordDefinitions_Words_WordId",
                        column: x => x.WordId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiGenerationCache_WordId_Provider",
                table: "AiGenerationCache",
                columns: new[] { "WordId", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearningSessions_ShownAt",
                table: "LearningSessions",
                column: "ShownAt");

            migrationBuilder.CreateIndex(
                name: "IX_LearningSessions_WordId",
                table: "LearningSessions",
                column: "WordId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewLogs_ReviewedAt",
                table: "ReviewLogs",
                column: "ReviewedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewLogs_WordId",
                table: "ReviewLogs",
                column: "WordId");

            migrationBuilder.CreateIndex(
                name: "IX_UserWordProgress_DueAt",
                table: "UserWordProgress",
                column: "DueAt");

            migrationBuilder.CreateIndex(
                name: "IX_UserWordProgress_WordId",
                table: "UserWordProgress",
                column: "WordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WordDefinitions_WordId_SortOrder",
                table: "WordDefinitions",
                columns: new[] { "WordId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Words_Difficulty",
                table: "Words",
                column: "Difficulty");

            migrationBuilder.CreateIndex(
                name: "IX_Words_FrequencyRank",
                table: "Words",
                column: "FrequencyRank");

            migrationBuilder.CreateIndex(
                name: "IX_Words_NormalizedWord",
                table: "Words",
                column: "NormalizedWord",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiGenerationCache");

            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "LearningSessions");

            migrationBuilder.DropTable(
                name: "ReviewLogs");

            migrationBuilder.DropTable(
                name: "UserLearningProfile");

            migrationBuilder.DropTable(
                name: "UserWordProgress");

            migrationBuilder.DropTable(
                name: "WordDefinitions");

            migrationBuilder.DropTable(
                name: "Words");
        }
    }
}
