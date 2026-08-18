using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CostumeManager
{

    public static class AppDialog
    {

        public static Window Host { get; set; }

        public static async Task<DialogResult> ShowAsync(string message, string title = "",
                                                         DialogButtons buttons = DialogButtons.OK,
                                                         DialogKind kind = DialogKind.Info,
                                                         string primaryText = null,
                                                         string closeText = null)
        {
            XamlRoot root = (Host?.Content)?.XamlRoot;
            if (root == null)
                return DialogResult.Cancel;

            bool confirm = buttons == DialogButtons.OKCancel || buttons == DialogButtons.YesNo;

            var dialog = new ContentDialog
            {
                XamlRoot = root,
                Title = string.IsNullOrWhiteSpace(title) ? null : title,

                Content = new TextBox
                {
                    Text = message ?? "",
                    IsReadOnly = true,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    BorderThickness = new Thickness(0),
                    Background = null,
                    MaxHeight = 340,
                },
                DefaultButton = ContentDialogButton.Primary,
            };

            switch (buttons)
            {
                case DialogButtons.YesNo:
                    dialog.PrimaryButtonText = "Yes";
                    dialog.CloseButtonText = "No";
                    break;
                case DialogButtons.OKCancel:
                    dialog.PrimaryButtonText = "OK";
                    dialog.CloseButtonText = "Cancel";
                    break;
                default:
                    dialog.CloseButtonText = "OK";
                    break;
            }

            if (!string.IsNullOrWhiteSpace(primaryText)) dialog.PrimaryButtonText = primaryText;
            if (!string.IsNullOrWhiteSpace(closeText)) dialog.CloseButtonText = closeText;

            ContentDialogResult r = await dialog.ShowAsync();

            if (!confirm) return DialogResult.OK;
            return r == ContentDialogResult.Primary
                ? (buttons == DialogButtons.YesNo ? DialogResult.Yes : DialogResult.OK)
                : (buttons == DialogButtons.YesNo ? DialogResult.No : DialogResult.Cancel);
        }
    }

    public enum DialogButtons { OK, OKCancel, YesNo }
    public enum DialogKind { Info, Warning, Error }
    public enum DialogResult { OK, Cancel, Yes, No }
}
