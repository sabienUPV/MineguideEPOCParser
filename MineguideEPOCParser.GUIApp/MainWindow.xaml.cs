using System.ComponentModel;
using System.Diagnostics;
using System.Windows;

namespace MineguideEPOCParser.GUIApp
{
    public partial class MainWindow : Window
    {
        private bool _isCleaningUp = false;
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
            if (_isSafeToClose) return;

            // 1. If we are already cleaning up, just keep the window canceled
            if (_isCleaningUp)
            {
                e.Cancel = true;
                return;
            }

            e.Cancel = true; // Stop the window from closing immediately
            _isCleaningUp = true; // Mark that we've started

            try
            {
                await MyMainControl.DisposeAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Cleanup failed: {ex.Message}");
            }
            finally
            {
                // 3. Flag that we are done
                _isSafeToClose = true;
                _isCleaningUp = false;
                // 4. IMPORTANT: Schedule the close to happen AFTER this method finishes.
                // This gives WPF a "breath" to finish the cancelled state before trying again.
                _ = Dispatcher.BeginInvoke(new Action(this.Close));
            }
        }
    }
}