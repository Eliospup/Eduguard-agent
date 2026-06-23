namespace EduGuardAgent.Models;

internal sealed record KioskCommonAppDefinition(
    string Id,
    string Name,
    string Icon,
    string ExeFileName,
    IReadOnlyList<string> CandidatePaths,
    string? DefaultArgs = null,
    IReadOnlyList<string>? SearchRoots = null);

/// <summary>
/// Well-known productivity apps commonly allowed in kiosk mode. Paths use
/// <c>%ProgramFiles%</c>, <c>%LocalAppData%</c>, etc. and are expanded at discovery time.
/// </summary>
internal static class KioskCommonAppCatalog
{
    public static IReadOnlyList<KioskCommonAppDefinition> All { get; } =
    [
        Browser("chrome", "Google Chrome", "🌐", "chrome.exe",
        [
            @"%ProgramFiles%\Google\Chrome\Application\chrome.exe",
            @"%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe",
            @"%LocalAppData%\Google\Chrome\Application\chrome.exe",
        ]),
        Browser("edge", "Microsoft Edge", "🌐", "msedge.exe",
        [
            @"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe",
            @"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe",
        ]),
        Browser("firefox", "Mozilla Firefox", "🦊", "firefox.exe",
        [
            @"%ProgramFiles%\Mozilla Firefox\firefox.exe",
            @"%ProgramFiles(x86)%\Mozilla Firefox\firefox.exe",
        ]),
        App("word", "Microsoft Word", "📝", "WINWORD.EXE", OfficePaths("WINWORD.EXE")),
        App("excel", "Microsoft Excel", "📊", "EXCEL.EXE", OfficePaths("EXCEL.EXE")),
        App("powerpoint", "Microsoft PowerPoint", "📽️", "POWERPNT.EXE", OfficePaths("POWERPNT.EXE")),
        App("onenote", "Microsoft OneNote", "📒", "ONENOTE.EXE", OfficePaths("ONENOTE.EXE")),
        App("notepad", "Notepad", "📄", "notepad.exe",
        [
            @"%SystemRoot%\System32\notepad.exe",
        ]),
        App("paint", "Paint", "🎨", "mspaint.exe",
        [
            @"%SystemRoot%\System32\mspaint.exe",
        ]),
        App("teams", "Microsoft Teams", "💬", "ms-teams.exe",
        [
            @"%LocalAppData%\Microsoft\WindowsApps\ms-teams.exe",
        ],
        searchRoots:
        [
            @"%ProgramFiles%\WindowsApps",
            @"%LocalAppData%\Microsoft\Teams",
            @"%ProgramFiles%\Microsoft\Teams",
        ]),
        App("teams-classic", "Microsoft Teams (classic)", "💬", "Teams.exe",
        [
            @"%LocalAppData%\Microsoft\Teams\current\Teams.exe",
            @"%ProgramFiles%\Microsoft\Teams\current\Teams.exe",
        ]),
        App("zoom", "Zoom", "📹", "Zoom.exe",
        [
            @"%ProgramFiles%\Zoom\bin\Zoom.exe",
            @"%ProgramFiles(x86)%\Zoom\bin\Zoom.exe",
            @"%AppData%\Zoom\bin\Zoom.exe",
        ]),
        App("vlc", "VLC media player", "▶️", "vlc.exe",
        [
            @"%ProgramFiles%\VideoLAN\VLC\vlc.exe",
            @"%ProgramFiles(x86)%\VideoLAN\VLC\vlc.exe",
        ]),
        App("acrobat", "Adobe Acrobat Reader", "📕", "AcroRd32.exe",
        [
            @"%ProgramFiles%\Adobe\Acrobat DC\Acrobat\Acrobat.exe",
            @"%ProgramFiles(x86)%\Adobe\Acrobat Reader DC\Reader\AcroRd32.exe",
            @"%ProgramFiles%\Adobe\Acrobat Reader DC\Reader\AcroRd32.exe",
        ]),
        App("libreoffice-writer", "LibreOffice Writer", "📝", "swriter.exe",
        [
            @"%ProgramFiles%\LibreOffice\program\swriter.exe",
            @"%ProgramFiles(x86)%\LibreOffice\program\swriter.exe",
        ]),
        App("libreoffice-calc", "LibreOffice Calc", "📊", "scalc.exe",
        [
            @"%ProgramFiles%\LibreOffice\program\scalc.exe",
            @"%ProgramFiles(x86)%\LibreOffice\program\scalc.exe",
        ]),
    ];

    private static KioskCommonAppDefinition Browser(
        string id,
        string name,
        string icon,
        string exe,
        IReadOnlyList<string> paths) =>
        new(id, name, icon, exe, paths, "--disable-features=TranslateUI");

    private static KioskCommonAppDefinition App(
        string id,
        string name,
        string icon,
        string exe,
        IReadOnlyList<string> paths,
        IReadOnlyList<string>? searchRoots = null) =>
        new(id, name, icon, exe, paths, SearchRoots: searchRoots);

    private static string[] OfficePaths(string exe) =>
    [
        $@"%ProgramFiles%\Microsoft Office\root\Office16\{exe}",
        $@"%ProgramFiles(x86)%\Microsoft Office\root\Office16\{exe}",
        $@"%ProgramFiles%\Microsoft Office\Office16\{exe}",
        $@"%ProgramFiles(x86)%\Microsoft Office\Office16\{exe}",
        $@"%ProgramFiles%\Microsoft Office\root\Office15\{exe}",
        $@"%ProgramFiles(x86)%\Microsoft Office\root\Office15\{exe}",
    ];
}
