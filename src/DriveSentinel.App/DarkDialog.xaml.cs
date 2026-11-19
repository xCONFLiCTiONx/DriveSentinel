using System.Windows;

namespace DriveSentinel;

public partial class DarkDialog : Window
{
    public bool UserConfirmed { get; private set; }

    public DarkDialog()
    {
        InitializeComponent();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        UserConfirmed = true;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        UserConfirmed = false;
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// Displays a dark-themed informational message dialog.
    /// </summary>
    public static void ShowInfo(Window? owner, string message, string title = "DriveSentinel")
    {
        var dlg = new DarkDialog();
        if (owner != null && owner.IsVisible) dlg.Owner = owner;
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.CancelButton.Visibility = Visibility.Collapsed;
        dlg.ConfirmButton.Content = "OK";
        dlg.ShowDialog();
    }

    /// <summary>
    /// Displays a dark-themed confirmation question dialog. Returns true if user clicks Confirm.
    /// </summary>
    public static bool Confirm(Window? owner, string message, string title = "Confirm Action", string confirmText = "Confirm", string cancelText = "Cancel")
    {
        var dlg = new DarkDialog();
        if (owner != null && owner.IsVisible) dlg.Owner = owner;
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.CancelButton.Visibility = Visibility.Visible;
        dlg.CancelButton.Content = cancelText;
        dlg.ConfirmButton.Content = confirmText;
        return dlg.ShowDialog() == true && dlg.UserConfirmed;
    }
}

