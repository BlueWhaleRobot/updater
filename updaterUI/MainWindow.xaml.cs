using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using updaterLib;

namespace updaterUI
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Task.Run(() =>
            {
                Thread.Sleep(2 * 1000);
                var updater = new Updater(AppDomain.CurrentDomain.BaseDirectory);
                updater.checkUpdate();
                updater.startUpdate((progress) => {
                    Dispatcher.Invoke(() =>
                    {
                        updateProgress.Value = 100 * progress;
                    });
                });
                Dispatcher.Invoke(() =>
                {
                    Close();
                });
            });
        }
    }
}
