namespace WitcherHub.Tests
{
    /// <summary>
    /// Where the sources are, for the tests that read markup and stylesheets
    /// rather than call code.
    ///
    /// Those tests run from bin/, so every one of them has to climb back out to
    /// the repository first. That climb was being written again in each file;
    /// this is the one copy.
    /// </summary>
    internal static class TestPaths
    {
        /// <summary>The repository root, found by looking for the web project.</summary>
        public static string Repository
        {
            get
            {
                var directory = new DirectoryInfo(AppContext.BaseDirectory);

                while (directory is not null &&
                       !Directory.Exists(Path.Combine(directory.FullName, "WitcherHub", "Pages")))
                {
                    directory = directory.Parent;
                }

                return directory?.FullName
                    ?? throw new DirectoryNotFoundException(
                        "Could not find the repository root above " + AppContext.BaseDirectory);
            }
        }

        /// <summary>The WitcherHub web project — Pages, wwwroot and the rest.</summary>
        public static string WebProject => Path.Combine(Repository, "WitcherHub");
    }
}
