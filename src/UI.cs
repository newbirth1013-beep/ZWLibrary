using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CadApp = ZwSoft.ZwCAD.ApplicationServices.Application;

namespace ZWLibrary
{
    public static class Settings
    {
        private static string ConfigDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZWLibrary"); } }
        public static UserSettings Load() { return SettingsFile.Load(ConfigDir); }
        public static Store Current { get { return new Store(Load().LibraryRoot); } }
        public static void Save(UserSettings value) { SettingsFile.Save(ConfigDir, value); }
    }
}
