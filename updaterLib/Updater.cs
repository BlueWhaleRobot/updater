using LibGit2Sharp;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using updaterLib.models;
using WinSCP;

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
            if (System.Version.Parse(remoteInfo.version) > System.Version.Parse(currentInfo.version))
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
            Directory.Delete(Path.Combine(baseDir, "updates"), true);
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
                        if (File.Exists(path))
                            File.Delete(path);
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        using (var fileStream = File.OpenWrite(path))
                        {
                            var buffer = new byte[1024 * 1024];
                            while(true){
                                int count = stream.Read(buffer, 0, 1024 * 1024);
                                if (count == 0)
                                    break;
                                fileStream.Write(buffer, 0, count);
                            }
                            fileStream.Flush();
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

        public bool makeManifest()
        {
            // check if this is a git repo
            int maxDepth = 5;
            int depth = 0;
            string currentDir = baseDir;
            string gitRootDir = null;
            while(depth < maxDepth)
            {
                var parentDir = Directory.GetParent(currentDir).FullName;
                var children = Directory.GetDirectories(parentDir).Select(x => new DirectoryInfo(x).Name).ToList();
                if (children.Contains(".git"))
                {
                    gitRootDir = parentDir;
                    break;
                }
                currentDir = parentDir;
                depth++;
            }

            if(gitRootDir == null)
            {
                logger.Error("Not a valid git repo");
                return false;
            }

            try {
                using (var repo = new Repository(gitRootDir))
                {
                    Manifest manifest = new Manifest();
                    manifest.version = "0.0.0";
                    manifest.updateInfo = "";

                    // read lastest commits
                    Regex rgx = new Regex(@"jump version: (\d+.\d+.\d+)", RegexOptions.IgnoreCase);
                    foreach (var commit in repo.Commits)
                    {

                        var matchRes = rgx.Match(commit.Message);
                        if (matchRes.Success && matchRes.Groups.Count > 1)
                        {
                            if (manifest.version == "0.0.0")
                                manifest.version = matchRes.Groups[1].Value;
                            else
                                break;
                        }else if(manifest.version != "0.0.0")
                        {
                            // commits between latest version and previous version
                            manifest.updateInfo += String.Format("{0}\n", commit.Message);
                        }
                    }
   
                    manifest.name = new DirectoryInfo(gitRootDir).Name;
                    var currentBranch = repo.Branches.Where(x => x.IsCurrentRepositoryHead).ToList()[0];
                    var origins = repo.Network.Remotes.Where(x => x.Name == "origin").ToList();
                    if(origins.Count() == 0)
                    {
                        logger.Error("The repo does not have a valid origin");
                        return false;
                    }

                    var origin = origins[0].PushUrl.Substring(
                        origins[0].PushUrl.IndexOf("://") + 3).Replace(".", "/");
                    if (origin.EndsWith("/git"))
                        origin = origin.Substring(0, origin.Length - 4);
                    if (origin.IndexOf("@") != -1)
                    {
                        origin = origin.Substring(origin.IndexOf("@") + 1);
                    }

                        
                    manifest.updateURI = String.Format("https://update.bwbot.org/{0}/{1}",
                        origin, currentBranch.FriendlyName);
                    // calculate md5sums
                    string[] allFiles = Directory.GetFiles(baseDir, "*.*", SearchOption.AllDirectories);
                    List<models.FileInfo> filesInfo = allFiles.Select(
                        x => new models.FileInfo {
                            path = MakeRelativePath(baseDir, x).Replace("\\", "/"),
                            md5sum = GetMD5(x),
                        }).ToList();
                    string[] updateIgnores = null;
                    using (var reader = new StreamReader(File.OpenRead(Path.Combine(baseDir, "update.ignore")))){
                        updateIgnores = reader.ReadToEnd().Split('\n');
                    }
                    // remove ignore files
                    List<string> filesToIgnore = new List<string>();
                    foreach(var ignore in updateIgnores)
                    {
                        filesToIgnore.AddRange(Directory.GetFiles(baseDir, ignore, SearchOption.AllDirectories));
                    }
                    filesToIgnore = filesToIgnore.Select(x => MakeRelativePath(baseDir, x).Replace("\\", "/")).ToList();
                    
                    manifest.files = filesInfo.Where(
                        x => !filesToIgnore.Contains(x.path) && x.path != "manifest.json").ToList();
                    manifest.files.Add(new models.FileInfo { path = "manifest.json", md5sum = manifest.version });
                    // generate manifest file
                    if (File.Exists(Path.Combine(baseDir, "manifest.json")))
                    {
                        File.Delete(Path.Combine(baseDir, "manifest.json"));
                    }
                    using (var manifestFile = File.OpenWrite(Path.Combine(baseDir, "manifest.json")))
                    {
                        var writer = new StreamWriter(manifestFile);
                        writer.Write(JsonConvert.SerializeObject(manifest, Formatting.Indented));
                        writer.Flush();
                    }

                    logger.Info(JsonConvert.SerializeObject(manifest, Formatting.Indented));
                    logger.Info("Create manifest succeed");
                }
            }
            catch(Exception e)
            {
                logger.Error(e, "Not a valid git repo");
            }

            return true;
        }

        public bool deploy()
        {
            checkUpdate();
            if (currentInfo == null)
            {
                logger.Error("No manifest found");
                return false;
            }

            if (remoteInfo != null && System.Version.Parse(currentInfo.version) < System.Version.Parse(remoteInfo.version))
            {
                logger.Error("Local version is behind released version");
                return false;
            }
            var options =  new SessionOptions
            {
                Protocol = Protocol.Sftp,
                HostName = "update.bwbot.org",
                UserName = "bwbot",
                GiveUpSecurityAndAcceptAnySshHostKey = true,
                Timeout = new TimeSpan(0, 0, 5),
                SshPrivateKeyPath= Path.Combine(baseDir, "bwbot.ppk")
            };
            
            using (var session = new Session())
            {
                var sftpLogPath = Path.Combine(baseDir, "logs", "sftp.log");
                session.SessionLogPath = sftpLogPath;
                try
                {
                    session.Open(options);
                    logger.Info("Connect to release server succeed");
                }catch(Exception e)
                {
                    logger.Error(e, "connect to update server failed");
                    return false;
                }
                
                string remoteBaseDir = "/home/bwbot/data/src/updateServer/packages/" +
                    currentInfo.updateURI.Replace("https://update.bwbot.org/", "");
                int uploadCount = 0;
                foreach(var file in currentInfo.files)
                {
                    string remoteDir = remoteBaseDir + "/" + GetRemoteDirectory(file.path);
                    if (!session.FileExists(remoteDir))
                    {
                        session.ExecuteCommand("mkdir -p " + remoteDir).Check();
                    }
                    try
                    {
                        session.PutFiles(
                            Path.Combine(baseDir, file.path.Replace("/", "\\")),
                            remoteBaseDir + "/" + file.path
                        ).Check();
                    }
                    catch(Exception e)
                    {
                        logger.Error(e, "deploy failed");
                    }
                    
                    uploadCount++;
                    logger.Info("Release progress {0} ", uploadCount * 100f / currentInfo.files.Count);
                }
                logger.Info("Release progress Complete! ");
            }
            return true;
        }

        /// <summary>
        /// Creates a relative path from one file or folder to another.
        /// </summary>
        /// <param name="fromPath">Contains the directory that defines the start of the relative path.</param>
        /// <param name="toPath">Contains the path that defines the endpoint of the relative path.</param>
        /// <returns>The relative path from the start directory to the end path or <c>toPath</c> if the paths are not related.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="UriFormatException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public static String MakeRelativePath(String fromPath, String toPath)
        {
            if (String.IsNullOrEmpty(fromPath)) throw new ArgumentNullException("fromPath");
            if (String.IsNullOrEmpty(toPath)) throw new ArgumentNullException("toPath");

            Uri fromUri = new Uri(fromPath);
            Uri toUri = new Uri(toPath);

            if (fromUri.Scheme != toUri.Scheme) { return toPath; } // path can't be made relative.

            Uri relativeUri = fromUri.MakeRelativeUri(toUri);
            String relativePath = Uri.UnescapeDataString(relativeUri.ToString());

            if (toUri.Scheme.Equals("file", StringComparison.InvariantCultureIgnoreCase))
            {
                relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            }

            return relativePath;
        }

        public string GetMD5(string filename)
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(filename))
                {
                    return Convert.ToBase64String(md5.ComputeHash(stream));
                }
            }
        }

        public string GetRemoteDirectory(string filename)
        {
            var pathList = filename.Split('/').ToList();
            pathList.RemoveAt(pathList.Count - 1);
            return String.Join("/", pathList);
        }

    }
}
