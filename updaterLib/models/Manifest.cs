using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace updaterLib.models
{
    public class Manifest
    {
        public string version = "0.0.0";
        public string name = "";
        public string updateURI = "";
        public string updateInfo ="";

        public List<FileInfo> files = null;
    }
}
