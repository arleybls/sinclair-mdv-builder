using System;
using MdvCore.Mdv;

namespace MdvApp;

/// <summary>
/// Holds the currently-open cartridge plus its unsaved-changes flag, and notifies
/// pages when either changes so they can refresh (e.g. enable/disable Save).
/// </summary>
internal static class AppState
{
    public static MdvCartridge? Current { get; private set; }

    /// <summary>True when the cartridge has changes not yet written to disk.</summary>
    public static bool IsDirty { get; private set; }

    /// <summary>File number whose sectors should be highlighted on the Sector Map, if any.</summary>
    public static byte? HighlightFileNumber { get; private set; }

    public static event Action? Changed;

    /// <summary>Set the open cartridge. A freshly created image is dirty; a loaded one is clean.</summary>
    public static void SetCurrent(MdvCartridge? cartridge, bool isDirty = false)
    {
        Current = cartridge;
        IsDirty = cartridge != null && isDirty;
        HighlightFileNumber = null;
        Changed?.Invoke();
    }

    /// <summary>Set (or clear) the file whose sectors are highlighted on the Sector Map.</summary>
    public static void SetHighlightFile(byte? fileNumber)
    {
        HighlightFileNumber = fileNumber;
        Changed?.Invoke();
    }

    /// <summary>Flag the open cartridge as having unsaved changes.</summary>
    public static void MarkDirty()
    {
        if (Current == null || IsDirty)
            return;
        IsDirty = true;
        Changed?.Invoke();
    }

    /// <summary>Clear the unsaved-changes flag after a successful save.</summary>
    public static void MarkSaved()
    {
        if (!IsDirty)
            return;
        IsDirty = false;
        Changed?.Invoke();
    }
}
