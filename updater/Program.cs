using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using updaterLib;

namespace updater
{
    class Program
    {
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        static void Main(string[] args)
        {
            var updater = new Updater(AppDomain.CurrentDomain.BaseDirectory);
            if (updater.checkUpdate())
            {
                logger.Info("new updates found");
            }
            else
            {
                logger.Info("no updates found");
                return;
            }
            updater.startUpdate((progress) => {
                if (progress == 1)
                    logger.Info("download complete, updating files ...");
                else
                    logger.Info("update progress {0}", progress * 100);
            });
            logger.Info("Update complete!");
        }
    }
}
