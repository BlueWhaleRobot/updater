using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using updaterLib;

namespace updaterUI
{
    /// <summary>
    /// UpdateInfo.xaml 的交互逻辑
    /// </summary>
    public partial class UpdateInfo : Window
    {
        private bool isUpdateAvaliable = false;
        private Updater updater;
        public UpdateInfo()
        {
            InitializeComponent();
            updater = new Updater(AppDomain.CurrentDomain.BaseDirectory);
            isUpdateAvaliable = updater.checkUpdate();
            currentVersion.Content = updater.getLocalInfo().version;
            latestVersion.Content = updater.getRemoteInfo().version;
            updateInfo.Content = updater.getRemoteInfo().updateInfo;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (!isUpdateAvaliable)
                return;
            new MainWindow(updater).Show();
            Close();
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
