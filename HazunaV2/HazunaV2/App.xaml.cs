using System.Windows;

namespace HazunaV2;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            MessageBox.Show(e.Exception.ToString(), "Error UI");
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            MessageBox.Show(e.ExceptionObject.ToString(), "Error Fatal");
    }
}