using Serilog;
using System.Windows.Threading;

namespace MineguideEPOCParser.GUIApp
{
    public static class UIUtilities
    {
        /// <summary>
        /// Perform a non-critical UI action, such as updating a label or a progress bar, so that if it fails the parsing execution does not stop.
        /// </summary>
        /// <param name="action"></param>
        /// <param name="exceptionLogMessage"></param>
        /// <param name="isAlwaysRunningInUIThread">
        /// false by default. If true, we know that this code will ALWAYS be run from the UI thread,
        /// which means that we don't need to invoke it from the Dispatcher. Only set to true if you know that for sure.
        /// </param>
        public static void PerformNonCriticalUIAction(DispatcherObject source, Action action, ILogger? logger, string exceptionLogMessage, bool isAlwaysRunningInUIThread = false)
        {
            try
            {
                // If we know that we are in the UI thread
                // (either because we asserted it in the parameter or because we actually checked with CheckAccess())
                // we can run the action directly
                if (isAlwaysRunningInUIThread || source.Dispatcher.CheckAccess())
                {
                    action();
                }
                else
                {
                    source.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            action();
                        }
                        catch (Exception ex)
                        {
                            // Ignore exceptions when updating the UI,
                            // because they are not critical and we need the parsing to continue
                            // Log the exception if needed
                            logger?.Error(ex, exceptionLogMessage);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                // Prevent exceptions when updating the UI from stopping the execution,
                // because they are not critical and we need the parsing to continue
                // Log the exception if needed
                logger?.Error(ex, exceptionLogMessage);
            }
        }
    }
}
