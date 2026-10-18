using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SSMUpdater;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
    }
}
