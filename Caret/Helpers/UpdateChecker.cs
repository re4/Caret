using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Caret.Helpers;

internal static partial class UpdateChecker
{
    private const string TagsUrl = "https://github.com/re4/Caret/tags";
    private const string ReleasesUrl = "https://github.com/re4/Caret/releases";

    public record UpdateResult(bool Available, string CurrentVersion, string LatestVersion);

    public static async Task<UpdateResult?> CheckAsync()
    {
        try
        {
            var current = Assembly.GetExecutingAssembly().GetName().Version;
            if (current == null) return null;

            string currentStr = current.ToString(3);

            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Caret-UpdateCheck");
            http.Timeout = TimeSpan.FromSeconds(10);

            string html = await http.GetStringAsync(TagsUrl);

            var match = LatestTagPattern().Match(html);
            if (!match.Success) return null;

            string latest = match.Groups[1].Value.TrimStart('v');

            if (!Version.TryParse(latest, out var latestVer))
                return null;

            var currentNorm = new Version(current.Major, current.Minor, current.Build);
            bool available = latestVer > currentNorm;

            return new UpdateResult(available, currentStr, latest);
        }
        catch
        {
            return null;
        }
    }

    public static void OpenReleasesPage()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ReleasesUrl)
            {
                UseShellExecute = true
            });
        }
        catch { }
    }

    [GeneratedRegex(@"/re4/Caret/releases/tag/(v?[\d]+\.[\d]+\.?[\d]*)")]
    private static partial Regex LatestTagPattern();
}
