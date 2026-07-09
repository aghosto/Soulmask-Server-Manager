using ModernWpf.Controls;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace SoulmaskServerManager.Controls
{
    public partial class ModifySsmNameDialog : ContentDialog
    {
        private readonly Server _server;
        private readonly ObservableCollection<Server> _allServers;

        public ModifySsmNameDialog(Server server, ObservableCollection<Server> allServers)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _allServers = allServers ?? throw new ArgumentNullException(nameof(allServers));
            DataContext = server;
            InitializeComponent();
            PrimaryButtonClick += OnPrimaryButtonClick;
        }

        private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            ErrorText.Visibility = Visibility.Collapsed;

            string newName = ModifyName.Text?.Trim();
            if (string.IsNullOrWhiteSpace(newName))
            {
                args.Cancel = true;
                return;
            }

            if (_allServers.Any(s => s != _server && s.ssmServerName == newName))
            {
                ErrorText.Visibility = Visibility.Visible;
                args.Cancel = true;
                return;
            }

            _server.ssmServerName = newName;
        }
    }
}
