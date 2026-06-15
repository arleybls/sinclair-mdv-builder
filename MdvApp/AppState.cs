using System;
using MdvCore.Mdv;

namespace MdvApp;

/// <summary>
/// Holds the currently-open cartridge and notifies pages when it changes,
/// so cached pages refresh after a new image is loaded.
/// </summary>
internal static class AppState
{
    public static MdvCartridge? Current { get; private set; }

    public static event Action? Changed;

    public static void SetCurrent(MdvCartridge? cartridge)
    {
        Current = cartridge;
        Changed?.Invoke();
    }
}
