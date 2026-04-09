using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MineguideEPOCParser.GUIApp.Utils
{
    public static class TextBoxBehaviors
    {
        public static readonly DependencyProperty SelectAllOnClickProperty =
            DependencyProperty.RegisterAttached(
                "SelectAllOnClick",
                typeof(bool),
                typeof(TextBoxBehaviors),
                new UIPropertyMetadata(false, OnSelectAllOnClickChanged));

        public static bool GetSelectAllOnClick(DependencyObject obj) => (bool)obj.GetValue(SelectAllOnClickProperty);
        public static void SetSelectAllOnClick(DependencyObject obj, bool value) => obj.SetValue(SelectAllOnClickProperty, value);

        private static void OnSelectAllOnClickChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBox textBox)
            {
                if ((bool)e.NewValue)
                {
                    textBox.MouseDoubleClick += TextBox_MouseDoubleClick;
                    textBox.GotKeyboardFocus += TextBox_GotKeyboardFocus;
                    textBox.PreviewMouseLeftButtonDown += TextBox_PreviewMouseLeftButtonDown;
                }
                else
                {
                    textBox.MouseDoubleClick -= TextBox_MouseDoubleClick;
                    textBox.GotKeyboardFocus -= TextBox_GotKeyboardFocus;
                    textBox.PreviewMouseLeftButtonDown -= TextBox_PreviewMouseLeftButtonDown;
                }
            }
        }

        private static void TextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.SelectAll();
                e.Handled = true;
            }
        }

        private static void TextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.SelectAll();
            }
        }

        private static void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                // If the textbox is NOT focused yet, we intercept the click to prevent the "flash"
                if (!textBox.IsKeyboardFocusWithin)
                {
                    e.Handled = true; // Stop the click from unselecting the text
                    textBox.Focus();  // Manually trigger focus (which fires GotKeyboardFocus -> SelectAll)
                }
            }
        }
    }
}