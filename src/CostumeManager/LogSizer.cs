using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace CostumeManager
{

    public sealed class LogSizer : ContentControl
    {
        public LogSizer()
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
            HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
            VerticalContentAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch;
            IsTabStop = false;
        }
    }
}
