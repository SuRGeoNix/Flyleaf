using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using FlyleafLib;
using Microsoft.Win32;

namespace Flyleaf;

internal static class FileAssociations
{
    internal const string Handler = "Flyleaf.Media";
    const string ApplicationName = "Flyleaf";
    const string ApplicationsKey = @"Software\RegisteredApplications";
    const string CapabilitiesKey = @"Software\SuRGeoNix\Flyleaf\Capabilities";
    const string ClassesKey = @"Software\Classes";
    const string HandlerKey = ClassesKey + @"\" + Handler;
    const string FileTypesKey = CapabilitiesKey + @"\FileAssociations";
    const string OpenVerbKey = HandlerKey + @"\shell\open";
    const string OpenServerKey = ClassesKey + @"\CLSID\" + ExplorerOpen.ClassId + @"\LocalServer32";

    internal static readonly (string Label, string[] Extensions)[] Groups =
    [
        ("Video", Extensions(MediaExtensions.Video.Except(MediaExtensions.Audio, StringComparer.OrdinalIgnoreCase))),
        ("Audio", Extensions(MediaExtensions.Audio)),
        ("Images & animations", Extensions(MediaExtensions.Images)),
        ("Playlists", FlyleafPlaylistSerializer.SupportedExtensions.ToArray()),
        ("Subtitles", Extensions(MediaExtensions.Subtitles))
    ];

    static string[] Extensions(IEnumerable<string> values) => values.Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(value => "." + value.ToLowerInvariant()).Order(StringComparer.Ordinal).ToArray();

    internal static bool TryNormalizeExtension(string? input, out string extension)
    {
        string value = input?.Trim() ?? "";
        if (value.StartsWith('.')) value = value[1..];
        if (value.Length is < 1 or > 64 || !char.IsAsciiLetterOrDigit(value[0]) ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
        {
            extension = "";
            return false;
        }
        extension = "." + value.ToLowerInvariant();
        return true;
    }

    internal static HashSet<string> ReadRegistration()
    {
        using var capabilities = Registry.CurrentUser.OpenSubKey(FileTypesKey);
        return capabilities?.GetValueNames().Where(extension => Equals(capabilities.GetValue(extension), Handler))
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new(StringComparer.OrdinalIgnoreCase);
    }

    internal static bool IsDefault(string extension)
    {
        const uint progId = 20; // ASSOCSTR_PROGID
        const uint executable = 2; // ASSOCSTR_EXECUTABLE
        var name = new StringBuilder(260);

        bool Matches(uint type, string? expected)
        {
            if (expected == null) return false;
            name.Clear();
            name.EnsureCapacity(expected.Length + 1);
            uint length = (uint)name.Capacity;
            return AssocQueryStringW(0, type, extension, null, name, ref length) == 0 &&
                string.Equals(name.ToString(), expected, StringComparison.OrdinalIgnoreCase);
        }

        return Matches(progId, Handler) || Matches(executable, Environment.ProcessPath);
    }

    internal static void Apply(IEnumerable<string> selected)
    {
        if (Environment.ProcessPath is not { } executable || Path.GetFileName(executable).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Start Flyleaf.exe to manage file associations.");
        ApplyRegistration(Registry.CurrentUser, executable, selected);
        SHChangeNotify(0x08000000, 0, 0, 0); // SHCNE_ASSOCCHANGED, SHCNF_IDLIST
    }

    internal static void ApplyRegistration(RegistryKey user, string executable, IEnumerable<string> selected)
    {
        var enabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string input in selected)
        {
            if (!TryNormalizeExtension(input, out var extension))
                throw new ArgumentException("Enter a single file extension.", nameof(selected));
            enabled.Add(extension);
        }
        string icon = $"\"{executable}\",0";
        using var currentHandler = enabled.Count > 0 ? user.CreateSubKey(HandlerKey) : user.OpenSubKey(HandlerKey, writable: true);
        if (currentHandler != null)
        {
            // Retain the handler after deselection so existing Windows defaults can still open it.
            currentHandler.SetValue("", "Flyleaf media file");
            using var defaultIcon = currentHandler.CreateSubKey("DefaultIcon");
            defaultIcon.SetValue("", icon);
            using var open = currentHandler.CreateSubKey(@"shell\open");
            using var command = open.CreateSubKey("command");
            command.SetValue("", OpenCommand(executable));
            RegisterOpenVerb(user, open, executable);
        }

        using var capabilities = user.CreateSubKey(CapabilitiesKey);
        capabilities.SetValue("ApplicationName", ApplicationName);
        capabilities.SetValue("ApplicationDescription", "Play media and open playlists in Flyleaf.");
        capabilities.SetValue("ApplicationIcon", icon);
        using var fileTypes = capabilities.CreateSubKey("FileAssociations");
        var extensions = enabled.Concat(fileTypes.GetValueNames().Where(extension =>
            Equals(fileTypes.GetValue(extension), Handler) && TryNormalizeExtension(extension, out var normalized)
            && string.Equals(extension, normalized, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var extension in extensions)
        {
            var openWithKey = $@"{ClassesKey}\{extension}\OpenWithProgids";
            if (enabled.Contains(extension))
            {
                fileTypes.SetValue(extension, Handler);
                using var openWith = user.CreateSubKey(openWithKey);
                openWith.SetValue(Handler, Array.Empty<byte>(), RegistryValueKind.None);
            }
            else
            {
                using var openWith = user.OpenSubKey(openWithKey, writable: true);
                openWith?.DeleteValue(Handler, throwOnMissingValue: false);
                fileTypes.DeleteValue(extension, throwOnMissingValue: false);
            }
        }

        using var applications = user.CreateSubKey(ApplicationsKey);
        if (enabled.Count > 0) applications.SetValue(ApplicationName, CapabilitiesKey);
        else if (Equals(applications.GetValue(ApplicationName), CapabilitiesKey)) applications.DeleteValue(ApplicationName);
    }

    static string OpenCommand(string executable) => $"\"{executable}\" \"%1\"";

    static void RegisterOpenVerb(RegistryKey user, RegistryKey open, string executable)
    {
        using var localServer = user.CreateSubKey(OpenServerKey);
        localServer.SetValue("", $"\"{executable}\"");
        using var dropTarget = open.CreateSubKey("DropTarget");
        dropTarget.SetValue("Clsid", ExplorerOpen.ClassId);
        open.SetValue("MultiSelectModel", "Player");
    }

    internal static void UpgradeOpenVerb()
    {
        if (Environment.ProcessPath is not { } executable) return;
        try
        {
            using var user = Registry.CurrentUser;
            using var open = user.OpenSubKey(OpenVerbKey, writable: true);
            using var command = open?.OpenSubKey("command");
            // Only upgrade associations already pointing at this installation.
            if (!string.Equals(command?.GetValue("") as string, OpenCommand(executable), StringComparison.OrdinalIgnoreCase)) return;
            using var dropTarget = open!.OpenSubKey("DropTarget");
            using var localServer = user.OpenSubKey(OpenServerKey);
            if (Equals(dropTarget?.GetValue("Clsid"), ExplorerOpen.ClassId) && Equals(open.GetValue("MultiSelectModel"), "Player") &&
                string.Equals(localServer?.GetValue("") as string, $"\"{executable}\"", StringComparison.OrdinalIgnoreCase))
                return;
            RegisterOpenVerb(user, open, executable);
            SHChangeNotify(0x08000000, 0, 0, 0); // SHCNE_ASSOCCHANGED, SHCNF_IDLIST
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // Playback can start even when Windows blocks changes to the association.
        }
    }

    internal static void OpenWindowsDefaults()
    {
        string page = ReadRegistration().Count > 0
            ? "ms-settings:defaultapps?registeredAppUser=" + Uri.EscapeDataString(ApplicationName)
            : "ms-settings:defaultapps";
        using var process = Process.Start(new ProcessStartInfo(page) { UseShellExecute = true });
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int AssocQueryStringW(uint flags, uint type, string association, string? extra, StringBuilder output, ref uint length);

    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);
}
