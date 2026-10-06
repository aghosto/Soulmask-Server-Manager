using ModernWpf.Controls;
using System.Collections.ObjectModel;

namespace SoulmaskServerManager.Controls
{
    /// <summary>
    /// Interaction logic for EditorSaveDialog.xaml
    /// </summary>
    public partial class EditorSaveDialog : ContentDialog
    {
        public EditorSaveDialog(ObservableCollection<Server> servers, Server? currentServer = null)
        {
            DataContext = servers;
            InitializeComponent();

            if (currentServer != null)
            {
                int currentIndex = servers.IndexOf(currentServer);
                if (currentIndex >= 0)
                    ServerCombo.SelectedIndex = currentIndex;
            }

            if (ServerCombo.SelectedIndex < 0 && servers.Count > 0)
                ServerCombo.SelectedIndex = 0;
        }

        public Server? GetServer() => ServerCombo.SelectedItem as Server;
    }
}
