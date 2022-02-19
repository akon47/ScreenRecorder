using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScreenRecorder.Extensions
{
    public static class PopupExtensions
    {
        /// <summary>
        /// Obtain the handle of the window used by the pop-up control
        /// It is recommended for use in the OnOpen Event Handler. The handle value changes each time the corresponding event is called.
        /// </summary>
        /// <returns>popup window handle</returns>
        public static IntPtr GetPopupWindowHandle(this System.Windows.Controls.Primitives.Popup popup)
        {
            /// There is no way to get the handle of the pop-up window in the usual way, so it is obtained by reflection.
            /// Popup.cs code may not work if changed!!!
            /// Last commit hash for code referenced: 58e7355
            /// https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/Primitives/Popup.cs
            try
            {
                var _secHelper = popup.GetType().GetField("_secHelper", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(popup);
                if (_secHelper?.GetType().GetProperty("Handle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(_secHelper) is IntPtr popupHandle)
                {
                    return popupHandle;
                }
                else
                {
                    return IntPtr.Zero;
                }
            }
            catch
            {
                return IntPtr.Zero;
            }
        }
    }
}
