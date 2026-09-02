using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace WitcherHub.Infrastructure.Services.Pdf
{
    public sealed class PlaywrightBrowserInstaller
    {
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static bool _doneForCurrentProcess;

        private readonly ILogger<PlaywrightBrowserInstaller> _logger;

        public PlaywrightBrowserInstaller(ILogger<PlaywrightBrowserInstaller> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// True when a browser is already on disk where Playwright will look for
        /// it, so nothing needs downloading.
        ///
        /// The image installs Chromium at build time into PLAYWRIGHT_BROWSERS_PATH.
        /// Without this check every process still shelled out to the installer on
        /// its first PDF — which needs the network and, on a container whose
        /// filesystem or user will not allow the write, fails and takes the
        /// request down with it.
        /// </summary>
        internal static bool AlreadyInstalled()
        {
            var path = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH");

            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return false;

            // The installer lays each browser down in its own directory. An empty
            // directory means the variable is set and the install never happened.
            return Directory.EnumerateDirectories(path, "chromium*").Any();
        }

        public async Task EnsureInstalledAsync(CancellationToken ct = default)
        {
            if (_doneForCurrentProcess)
                return;

            await Gate.WaitAsync(ct);

            try
            {
                if (_doneForCurrentProcess)
                    return;

                if (AlreadyInstalled())
                {
                    _logger.LogInformation(
                        "Chromium is already installed at {Path}; skipping the download.",
                        Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH"));

                    _doneForCurrentProcess = true;
                    return;
                }

                _logger.LogInformation("Ensuring Playwright Chromium browser is installed.");

                await Task.Run(() =>
                {
                    Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
                }, ct);

                _doneForCurrentProcess = true;

                _logger.LogInformation("Playwright Chromium browser is ready.");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Playwright browser installation was canceled.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Playwright browser installation failed.");
                throw;
            }
            finally
            {
                Gate.Release();
            }
        }
    }
}
