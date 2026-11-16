using Microsoft.Win32;
using VocabularyCoach.Infrastructure.SystemIntegration;

namespace VocabularyCoach.Infrastructure.Tests.SystemIntegration;

public class WindowsStartupManagerTests
{
    [Fact]
    public void SetEnabled_WritesQuotedCommand_AndDisableRemovesIt()
    {
        var valueName = "DesktopVocabularyCoach.Test." + Guid.NewGuid().ToString("N");
        var manager = new WindowsStartupManager(valueName, @"C:\Apps\VocabularyCoach.exe");

        try
        {
            Assert.False(manager.IsEnabled());

            manager.SetEnabled(true);

            Assert.True(manager.IsEnabled());
            using (var key = Registry.CurrentUser.OpenSubKey(
                       @"Software\Microsoft\Windows\CurrentVersion\Run", writable: false))
            {
                Assert.Equal(
                    "\"C:\\Apps\\VocabularyCoach.exe\" --startup",
                    key?.GetValue(valueName) as string);
            }

            manager.SetEnabled(false);
            Assert.False(manager.IsEnabled());
        }
        finally
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key?.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }

    [Fact]
    public void SetEnabled_ThrowsWithoutExecutablePath()
    {
        var manager = new WindowsStartupManager("DesktopVocabularyCoach.Test", executablePath: " ");

        Assert.Throws<InvalidOperationException>(() => manager.SetEnabled(true));
    }
}
