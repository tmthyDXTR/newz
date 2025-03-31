using System;
using System.Windows;
using System.Windows.Threading;

namespace Nius
{
    public partial class App : Application
    {
        public App()
        {
            // Add handler for unhandled exceptions
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // Log the exception
            System.Diagnostics.Debug.WriteLine($"Unhandled UI exception: {e.Exception}");
            
            // Show error message
            MessageBox.Show($"An error occurred: {e.Exception.Message}\n\nDetails: {e.Exception}", 
                "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
            
            // Prevent application from crashing
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            System.Diagnostics.Debug.WriteLine($"Unhandled domain exception: {ex}");
            
            MessageBox.Show($"A critical error occurred: {ex?.Message}\n\nThe application needs to close.", 
                "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
