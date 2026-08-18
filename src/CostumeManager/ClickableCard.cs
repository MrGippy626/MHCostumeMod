using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace CostumeManager
{

    public sealed class ClickableCard : Button
    {
        public ClickableCard()
        {

            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
            Loaded += (s, e) =>
                ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        }
    }
}
