﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web.Configuration;
using System.Xml;
using DotNetNuke.Application;
using DotNetNuke.Common;
using DotNetNuke.Common.Utilities;
using DotNetNuke.Data;
using Assembly = System.Reflection.Assembly;

namespace DNN.Modules.SecurityAnalyzer.Components
{
    public class Utility
    {
        private static readonly IList<Regex> ExcludedFilePathRegexList = new List<Regex>()
        {
            new Regex(Regex.Escape("\\App_Data\\ClientDependency"), RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(Regex.Escape("\\App_Data\\Search"), RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(Regex.Escape("\\d+-System\\Cache\\Pages"), RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(Regex.Escape("\\d+-System\\Thumbnailsy"), RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(Regex.Escape("\\Portals\\_default\\Logs"), RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(Regex.Escape("\\App_Data\\_imagecache"), RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(Regex.Escape(AppDomain.CurrentDomain.BaseDirectory + "Default.aspx"), RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(Regex.Escape(AppDomain.CurrentDomain.BaseDirectory + "Default.aspx.cs"), RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(Regex.Escape(AppDomain.CurrentDomain.BaseDirectory + "web.config"), RegexOptions.Compiled | RegexOptions.IgnoreCase),
        };

        private const long MaxFileSize = 1024*1024*10; //10M

        private const int ModifiedFilesCount = 50;

        /// <summary>
        ///     delete unnedded installwizard files
        /// </summary>
        public static void CleanUpInstallerFiles()
        {
            var files = new List<string>
            {
                "DotNetNuke.install.config",
                "DotNetNuke.install.config.resources",
                "InstallWizard.aspx",
                "InstallWizard.aspx.cs",
                "InstallWizard.aspx.designer.cs",
                "UpgradeWizard.aspx",
                "UpgradeWizard.aspx.cs",
                "UpgradeWizard.aspx.designer.cs",
                "Install.aspx",
                "Install.aspx.cs",
                "Install.aspx.designer.cs",
            };

            foreach (var file in files)
            {
                try
                {
                    FileSystemUtils.DeleteFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Install\\" + file));
                }
                catch (Exception)
                {
                    //do nothing.
                }
            }
        }

        /// <summary>
        ///     search all files in the website for matching text
        /// </summary>
        /// <param name="searchText">the matching text</param>
        /// <returns>ienumerable of file names</returns>
        public static IEnumerable<string> SearchFiles(string searchText)
        {
            try
            {
                var fileList = GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.*", SearchOption.AllDirectories);
                var queryMatchingFiles =
                    from file in fileList
                    let fileText = GetFileText(file)
                    let fileInfo = new FileInfo(file)
                    where fileText.IndexOf(searchText, StringComparison.InvariantCultureIgnoreCase) > -1
                    select fileInfo.Name + " (" + fileInfo.LastWriteTime.ToString(CultureInfo.InvariantCulture) + ")";
                return queryMatchingFiles;
            }
            catch
            {
                //suppress any unexpected error
            }
            return null;
        }

        /// <summary>
        ///     search all website files for files with a potential dangerous extension
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<string> FindUnexpectedExtensions(IList<string> invalidFolders)
        {
            var files = GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.*", SearchOption.AllDirectories, invalidFolders)
            .Where(s => s.EndsWith(".asp", StringComparison.InvariantCultureIgnoreCase) || s.EndsWith(".php", StringComparison.InvariantCultureIgnoreCase));
            return files;
        }

        /// <summary>
        ///     search all website files which are hidden or system.
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<string> FineHiddenSystemFiles()
        {
            var files = GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.*", SearchOption.AllDirectories)
            .Where(f =>
            {
                if (Path.GetFileName(f)?.Equals("thumbs.db", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return false;
                }

                var attributes = File.GetAttributes(f);
                return (attributes & FileAttributes.Hidden) != 0 || (attributes & FileAttributes.System) != 0;
            });
            return files;
        }

        public static string SearchDatabase(string searchText)
        {
            var results = "";
            var dataProvider = DataProvider.Instance();
            var rowCount = 0;
            try
            {
                var dr = dataProvider.ExecuteReader("SecurityAnalyzer_SearchAllTables", searchText);
                while (dr.Read())
                {
                    rowCount = rowCount + 1;
                    results = results + dr["ColumnName"] + ":" + dr["ColumnValue"] + "<br/>";
                }
            }
            catch
            {
                // ignore
            }
            results = "Database instances Found:" + rowCount + "<br/>" + results;
            return results;
        }

        public static XmlDocument LoadFileSumData()
        {
            using (
                var stream =
                    Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream("DNN.Modules.SecurityAnalyzer.Resources.sums.resources"))
            {
                if (stream != null)
                {
                    var xmlDocument = new XmlDocument();
                    xmlDocument.Load(stream);

                    return xmlDocument;
                }
                else
                {
                    return null;
                }
            }
        }

        public static string GetFileCheckSum(string fileName)
        {
            using (var cryptographyProvider = CreateCryptographyProvider())
            {
                if (cryptographyProvider != null)
                {
                    using (var stream = File.OpenRead(fileName))
                    {
                        return BitConverter.ToString(cryptographyProvider.ComputeHash(stream)).Replace("-", "")
                            .ToLowerInvariant();
                    }
                }
            }

            return string.Empty;
        }

        public static string GetApplicationVersion()
        {
            return DotNetNukeContext.Current.Application.Version.ToString(3);
        }

        public static string GetApplicationType()
        {
            switch (DotNetNukeContext.Current.Application.Name)
            {
                case "DNNCORP.CE":
                    return "Platform";
                case "DNNCORP.XE":
                case "DNNCORP.PE":
                    return "Content";
                case "DNNCORP.SOCIAL":
                    return "Social";
                default:
                    return "Platform";
            }
        }

        public static IList<FileInfo> GetLastModifiedFiles()
        {
            var files = GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.*", SearchOption.AllDirectories)
                .Where(f => !ExcludedFilePathRegexList.Any(r => r.IsMatch(f)))
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .Take(ModifiedFilesCount).ToList();

            return files;
        }

        public static IList<FileInfo> GetLastModifiedExecutableFiles()
        {
            var executableExtensions = new List<string>() {".asp", ".aspx", ".php"};
            var files = GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.*", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var extension = Path.GetExtension(f);
                    return extension != null && executableExtensions.Contains(extension.ToLowerInvariant());
                }).ToList();
            files.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Default.aspx.cs"));
            files.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web.config"));

            var defaultPage = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Default.aspx");
            if (!files.Contains(defaultPage))
            {
                files.Add(defaultPage);
            }

            return files
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .Take(ModifiedFilesCount).ToList();

        }

        private static string GetFileText(string name)
        {
            var fileContents = String.Empty;
            try
            {
                // If the file has been deleted since we took  
                // the snapshot, ignore it and return the empty string. 
                if (IsReadable(name))
                {
                    fileContents = File.ReadAllText(name);
                }
            }
            catch (Exception)
            {

                //might be a locking issue
            }

            return fileContents;
        }

        private static bool IsReadable(string name)
        {
            if (!File.Exists(name))
            {
                return false;
            }

            var file = new FileInfo(name);
            if (file.Length > MaxFileSize) //when file large than 10M, then don't read it.
            {
                return false;
            }

            return true;
        }

        private static SHA256 CreateCryptographyProvider()
        {
            try
            {
                var property = typeof(CryptoConfig).GetProperty("AllowOnlyFipsAlgorithms", BindingFlags.Public | BindingFlags.Static);
                if (property == null)
                {
                    return SHA256.Create();
                }

                if ((bool)property.GetValue(null, null))
                {
                    return SHA256.Create("System.Security.Cryptography.SHA256CryptoServiceProvider");
                }

                return SHA256.Create("System.Security.Cryptography.SHA256Cng");
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Recursively finds file
        /// </summary>
        /// <returns></returns>
        private static IEnumerable<string> GetFiles(string path, string searchPattern, SearchOption searchOption)
        {
            IList<string> invalidFolders = new List<string>();
            return GetFiles(path, searchPattern, searchOption, invalidFolders);
        } 

        /// <summary>
        /// Recursively finds file
        /// </summary>
        /// <returns></returns>
        private static IEnumerable<string> GetFiles(string path, string searchPattern, SearchOption searchOption, IList<string> invalidFolders)
        {
            try
            {
                //Looking at the root folder only. There should not be any permission issue here.
                var files = Directory.GetFiles(path, searchPattern, SearchOption.TopDirectoryOnly).ToList();

                if (searchOption == SearchOption.AllDirectories)
                {
                    var folders = Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly);
                    foreach (var folder in folders)
                    {
                        //recursive call to the same method
                        var fs = GetFiles(folder, searchPattern, searchOption, invalidFolders);
                        files.AddRange(fs);
                    }
                }

                return files;
            }
            catch (Exception)
            {
                invalidFolders.Add(path);
                return new List<string>();
            }            
        }

        //DNN-10258: Site loses ability to edit content after Security Patch install
        //DNN-10259: Site loses ability to add pages after Security Patch install
        public static bool UpdateTelerikSkinsSettings()
        {
            const string skinAssemblyKey = "Telerik.Web.SkinsAssembly";
            var assemblyFile = Path.Combine(Globals.ApplicationMapPath, "bin\\Telerik.Web.UI.Skins.dll");
            if (File.Exists(assemblyFile))
            {
                
                var asmFullName = Assembly.LoadFile(assemblyFile).GetName().ToString();

                var appSetting = Config.GetSetting(skinAssemblyKey);
                if (string.IsNullOrEmpty(appSetting) || !appSetting.Equals(asmFullName, StringComparison.InvariantCultureIgnoreCase))
                {
                    //save the current config file
                    Config.BackupConfig();

                    //decrypt the web.config if needed.
                    string providerName;
                    var decrypted = Utility.DecryptConfigFile(out providerName);

                    //open the web.config
                    var config = Config.Load();

                    Config.AddAppSetting(config, skinAssemblyKey, asmFullName);
                    Config.Save(config);

                    if (decrypted)
                    {
                        EncryptConfigFile(providerName);
                    }

                    return true;
                }
            }

            return false;
        }

        public static bool DecryptConfigFile(out string providerName)
        {
            providerName = string.Empty;
            var config = WebConfigurationManager.OpenWebConfiguration("~/");
            var section = config.GetSection("appSettings");
            if (section != null && section.SectionInformation.IsProtected)
            {
                providerName = section.SectionInformation.ProtectionProvider.Name;
                section.SectionInformation.UnprotectSection();
                config.Save();

                return true;
            }

            return false;
        }

        public static void EncryptConfigFile(string providerName)
        {
            var config = WebConfigurationManager.OpenWebConfiguration("~/");
            var section = config.GetSection("appSettings");
            if (section != null)
            {
                section.SectionInformation.ProtectSection(providerName);
                config.Save();
            }
        }
    }
}