using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace Volumy.Interop;

// WinUI 3 only lets a control set its own cursor (ProtectedCursor is protected), so these
// tiny subclasses do it the supported way. Use them in XAML as <ui:HandButton>, etc.

public class HandButton : Button
{
    public HandButton() => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
}

public class HandComboBox : ComboBox
{
    public HandComboBox() => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
}


public class HandCheckBox : CheckBox
{
    public HandCheckBox() => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
}
