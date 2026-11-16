using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VocabularyCoach.Infrastructure.Persistence;

/// <summary>
/// 供 `dotnet ef migrations` 使用的设计时工厂。
/// 迁移只需要上下文结构，不读取用户数据库内容，使用临时文件即可。
/// </summary>
public sealed class VocabularyCoachDbContextFactory : IDesignTimeDbContextFactory<VocabularyCoachDbContext>
{
    public VocabularyCoachDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<VocabularyCoachDbContext>()
            .UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), "vocabulary-design.db")}")
            .Options;

        return new VocabularyCoachDbContext(options);
    }
}
