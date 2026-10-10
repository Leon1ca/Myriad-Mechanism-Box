using System.Globalization;
using System.Text.Json.Serialization;

namespace CustomsClearanceConsole;

/// <summary>
/// Decides whether a completed capture may be written back. A queued completion from an
/// invalidated session (batch switch / list clear / exit) is filtered by the session
/// registry, while this guards the value identity: only a saved capture whose declaration
/// number matches the session may update the current batch. A previously saved session
/// keeps its original screenshot on any later failure.
/// </summary>
internal static class BrowserCapturePolicy
{
    public static bool CanBackfill(string sessionDeclarationNo, string resultState, string resultDeclarationNo) =>
        resultState == "saved" &&
        !string.IsNullOrWhiteSpace(sessionDeclarationNo) &&
        string.Equals(sessionDeclarationNo, resultDeclarationNo, StringComparison.Ordinal);
}
