using System;

namespace Zenith.Core;

public interface IHotkeyService
{
    event EventHandler? HotkeyTriggered;
    void RegisterGlobalHotkey();
    void UnregisterGlobalHotkey();
}
