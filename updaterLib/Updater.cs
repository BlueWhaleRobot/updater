using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using updaterLib.models;

namespace updaterLib
{
    public class Updater
    {
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        private string baseDir;
        private Manifest currentInfo;
        private Manifest remoteInfo;
        private int updateCount = 0;
        private bool completeFlag = false;

        public Updater(string baseDir)
        {
            this.baseDir = baseDir;
        }

        public bool checkUpdate()
        {
            // load manifest.json
            string info = "";
            try
            {
                using (var stream = File.OpenRead(Path.Combine(baseDir, "manifest.json")))
                {
                    info = new StreamReader(stream).ReadToEnd();
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "load manifest file failed");
                return false;
            }

            try
            {
                currentInfo = JsonConvert.DeserializeObject<Manifest>(info);
            }
            catch (Exception e)
            {
                logger.Error(e, "invalid manifest file");
                return false;
            }

            // load remote manifest
            using (var client = new HttpClient())
            {
                try
                {
                    info = client.GetStringAsync(currentInfo.updateURI + "/manifest.json").GetAwaiter().GetResult();
                }
                catch (Exception e)
                {
                    logger.Error(e, "get remote manifest failed");
                    return false;
                }

                logger.Info("get remote manifest success");
            }
            logger.Info(info);

            try
            {
                remoteInfo = JsonConvert.DeserializeObject<Manifest>(info);
            }
            catch (Exception e)
            {
                logger.Error(e, "invalid remote manifest");
                return false;
            }
            if (Version.Parse(remoteInfo.version) > Version.Parse(currentInfo.version))
            {
                return true;
            }
            return false;

        }

        public bool startUpdate(Action<double> progressCallback)
        {
            if(currentInfo == null || remoteInfo == null)
            {
                logger.Error("no manifest loaded, call checkupdate first");
                return false;
            }

            // Check files with different hash
            List<string> filesToDownload = new List<string>();
            List<string> filesToDelete = new List<string>();
            foreach(var file in remoteInfo.files)
            {
                var oldFile = currentInfo.files.Where(x => x.path == file.path).ToList();
                if (oldFile.Count == 0 || file.md5sum != oldFile[0].md5sum)
                    filesToDownload.Add(file.path);
            }
            foreach(var file in currentInfo.files)
            {
                if (remoteInfo.files.Where(x => x.path == file.path).ToList().Count == 0)
                    filesToDelete.Add(file.path);
            }
            // make a update directory and start download
            Directory.CreateDirectory(Path.Combine(baseDir, "updates"));
            List<Task<bool>> downloadTasks = new List<Task<bool>>();
            foreach(var file in filesToDownload)
            {
                downloadTasks.Add(downloadFile(remoteInfo.updateURI + "/" + file, Path.Combine(baseDir, "updates", file)));   
            }

            Task.Run(() => {
                while (!completeFlag)
                {
                    if (filesToDownload.Count != 0)
                        progressCallback(updateCount * 1f / filesToDownload.Count);
                    else
                        progressCallback(1);
                    Thread.Sleep(500);
                }
            });

            try
            {
                Task.WaitAll(downloadTasks.ToArray());
            }catch(Exception e)
            {
                logger.Error(e, "download file failed");
                completeFlag = true;
                progressCallback(1);
                return false;
            }
            logger.Info("download complete");
            progressCallback(1);
            completeFlag = true;
            // delete files
            foreach(var file in filesToDelete)
            {
                File.Delete(Path.Combine(baseDir, file));
            }
            logger.Info("Delete old files");
            // move downloaded files
            foreach(var file in filesToDownload)
            {
                if (File.Exists(Path.Combine(baseDir, file)))
                    File.Delete(Path.Combine(baseDir, file));
                File.Move(Path.Combine(baseDir, "updates", file), Path.Combine(baseDir, file));
            }
            logger.Info("Move new files");
            Directory.Delete(Path.Combine(baseDir, "updates"));
            return true;
        }

        public Task<bool> downloadFile(string url, string path)
        {
            return Task.Run(async () =>
            {
                using (var client = new HttpClient())
                {
                    Stream stream = null;
                    try
                    {
                        stream = await client.GetStreamAsync(url);
                    }
                    catch(Exception e)
                    {
                        logger.Error(e, "network error");
                        throw e;
                    }
                    try
                    {
                        using (var fileStream = File.OpenWrite(path))
                        {
                            var buffer = new byte[1024 * 1024];
                            while(true){
                                int count = stream.Read(buffer, 0, 1024 * 1024);
                                if (count == 0)
                                    break;
                                fileStream.Write(buffer, 0, count);
                            }
                        }
                    }
                    catch(Exception e)
                    {
                        logger.Error(e, "cannot create files");
                        throw e;
                    }
                    logger.Info("saving file to {0}", path);
                    updateCount++;
                    return true;
                }
            });
        }

        public Manifest getLocalInfo()
        {
            return currentInfo;
        }

        public Manifest getRemoteInfo()
        {
            return remoteInfo;
        }

    }
}
