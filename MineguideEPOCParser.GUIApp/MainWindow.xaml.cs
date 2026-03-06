using System.ComponentModel;
using System.Windows;

namespace MineguideEPOCParser.GUIApp
{
    public partial class MainWindow : Window
    {
        private bool _isSafeToClose = false;

        public MainWindow()
        {
            InitializeComponent();
        }

        /*
         * WPF has a tricky quirk: the Window_Closing event is strictly synchronous.
         * If you try to await MyMainControl.DisposeAsync() inside it,
         * the Window will finish closing and destroy the app before the async disposal is actually done!
         * 
         * To fix this, we use the "Cancel and Defer" pattern. We cancel the initial close,
         * run the async cleanup, and then tell the window to close for real.
         */
        private async void Window_Closing(object sender, CancelEventArgs e)
        {
            // If we've already done the cleanup, let the window close naturally
            if (_isSafeToClose) return;

            // 1. Stop the window from closing immediately
            e.Cancel = true;

            // 2. Await the cleanup in your UserControl
            await MyMainControl.DisposeAsync();

            // 3. Flag that we are done, and trigger the close again
            _isSafeToClose = true;
            this.Close();
        }
    }
}