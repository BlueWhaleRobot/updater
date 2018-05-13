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
        }

        public MainWindow(Updater updater)
        {
            InitializeComponent();
            Task.Run(() =>
            {
                updater.startUpdate((progress) => {
                    Dispatcher.Invoke(() =>
                    {
                        updateProgress.Value = 100 * progress;
                    });
                });
                MessageBox.Show("软件已经成功更新", "更新完成");
                Dispatcher.Invoke(() =>
                {
                    Close();
                });
            });

        }
    }
}
