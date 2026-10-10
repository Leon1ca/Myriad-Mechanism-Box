using System.Globalization;
using System.Text.Json.Serialization;

namespace CustomsClearanceConsole;

public sealed class AppState
{
    public const int CurrentUiSchemaVersion = 6;
    public int UiSchemaVersion { get; set; } = CurrentUiSchemaVersion;
    public string LastFolder { get; set; } = "";
    public string ScreenshotFolder { get; set; } = "";
    public int PageSize { get; set; } = 50;
    public List<DeclarationRecord> Records { get; set; } = [];
}
