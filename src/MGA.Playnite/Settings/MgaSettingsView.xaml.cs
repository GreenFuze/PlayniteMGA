using System.Windows;
using System.Windows.Controls;

namespace MGA.Playnite.Settings
{
    public partial class MgaSettingsView : UserControl
    {
        public MgaSettingsView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// A PasswordBox deliberately does not expose its content as a bindable
        /// dependency property, so the typed key is pushed to the view model by
        /// hand. Keeping it in a PasswordBox rather than a TextBox is the point:
        /// the key is not rendered on screen, so it cannot be read over a
        /// shoulder or captured in a screenshot of the settings page.
        /// </summary>
        private void AccessKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MgaSettingsViewModel;
            var box = sender as PasswordBox;
            if (viewModel == null || box == null)
            {
                return;
            }
            viewModel.PendingAccessKey = box.Password;
        }
    }
}
