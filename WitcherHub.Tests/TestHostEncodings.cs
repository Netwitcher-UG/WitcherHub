using System.Runtime.CompilerServices;
using System.Text;

namespace WitcherHub.Tests
{
    /// <summary>
    /// The legacy code pages, registered for the test host as the application
    /// registers them for itself.
    ///
    /// The contract template is stored in Windows-1252, and the generator reads
    /// it by sniffing the encoding — so producing a contract needs code page
    /// 1252 to exist. .NET does not carry it: <c>Program.cs</c> calls
    /// <c>Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)</c> on
    /// the first line of startup, and the running application is therefore fine.
    ///
    /// A test host has no <c>Program.cs</c>. Without this, every test that
    /// generates a contract fails with "No data is available for encoding 1252"
    /// — a message about the harness, reported as though the assistant had
    /// refused. The tests would be measuring a gap in their own setup.
    ///
    /// A module initializer rather than a fixture: it has to run before any test
    /// class is constructed, and no test should have to remember it.
    /// </summary>
    internal static class TestHostEncodings
    {
        [ModuleInitializer]
        internal static void Register() =>
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
}
