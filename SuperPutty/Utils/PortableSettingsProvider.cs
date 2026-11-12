/*
 * Copyright (c) 2009 - 2015 Jim Radford http://www.jimradford.com
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions: 
 * 
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */

using System;
using System.Configuration;
using System.Windows.Forms;
using System.IO;
using System.Xml;
using log4net;
using System.Collections;
using System.Linq;

namespace SuperPutty.Utils
{
    /// <summary>
    /// PortableSettingsProvider
    /// 
    /// Based on 
    /// http://www.codeproject.com/Articles/20917/Creating-a-Custom-Settings-Provider
    /// </summary>
    public class PortableSettingsProvider : SettingsProvider
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(PortableSettingsProvider));

        private static bool ForceRoamingSettings = Convert.ToBoolean(ConfigurationManager.AppSettings["SuperPuTTY.ForceRoamingSettings"] ?? "True");

        public const string SettingsRoot = "Settings";

        private XmlDocument settingsXML;

        internal sealed class SettingsRecoveryNotification
        {
            internal SettingsRecoveryNotification(string settingsFilePath, string backupFilePath, string preservedFilePath)
            {
                SettingsFilePath = settingsFilePath;
                BackupFilePath = backupFilePath;
                PreservedFilePath = preservedFilePath;
            }

            internal string SettingsFilePath { get; private set; }
            internal string BackupFilePath { get; private set; }
            internal string PreservedFilePath { get; private set; }
        }

        internal SettingsRecoveryNotification RecoveryNotification { get; private set; }

        public override void Initialize(string name, System.Collections.Specialized.NameValueCollection config)
        {
            base.Initialize(this.ApplicationName, config);
        }

        public override string ApplicationName
        {
            get
            {
                if (Application.ProductName.Trim().Length > 0)
                {
                    return Application.ProductName;
                }
                else
                {
                    FileInfo fi = new FileInfo(Application.ExecutablePath);
                    return fi.Name.Substring(0, fi.Name.Length - fi.Extension.Length);
                }
            }
            set { }
        }


        /// <summary>
        /// Return a list of possible locations for the settings file.  If non are found, create the 
        /// default in the first location
        /// </summary>
        /// <returns></returns>
        public virtual string[] GetAppSettingsPaths()
        {
            string[] paths = new string[2];
            paths[0] = Environment.GetEnvironmentVariable("USERPROFILE");
            paths[1] = Path.GetDirectoryName(Application.ExecutablePath);
            return paths;
        }

        public virtual string GetAppSettingsFileName()
        {
            return ApplicationName + ".settings";
        }

        /// <summary>
        /// Return first existing file path or the first if none found
        /// </summary>
        /// <returns></returns>
        string GetAppSettingsFilePath()
        {
            string[] paths = GetAppSettingsPaths();
            string fileName = GetAppSettingsFileName();

            string path = Path.Combine(paths[0], fileName);
            foreach (string dir in paths)
            {
                string filePath = Path.Combine(dir, fileName);
                if (SettingsPathExists(filePath))
                {
                    path = filePath;
                    break;
                }
            }

            // A backup-only profile should recover in its original location.
            if (!SettingsPathExists(path))
            {
                foreach (string dir in paths)
                {
                    string filePath = Path.Combine(dir, fileName);
                    if (SettingsPathExists(filePath + ".bak")) return filePath;
                }
            }
            return path;
        }

        private static bool SettingsPathExists(string path)
        {
            // File.Exists also returns false for access failures, which could silently
            // select a different profile or allow defaults to replace an unreadable file.
            try { File.GetAttributes(path); return true; }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }

        public string SettingsFilePath { get; private set; }

        public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection collection)
        {
            // Load before mutating anything, and propagate unrecoverable load failures.
            XmlDocument document = SettingsXML;
            foreach (SettingsPropertyValue propVal in collection)
            {
                SetValue(propVal);
            }

            try
            {             
                SaveAtomically(document, SettingsFilePath);
            }
            catch(Exception ex){
                Log.Error("Error saving settings", ex);
                throw new ConfigurationErrorsException("Unable to save settings. Check that the settings folder is writable.", ex);
            }
        }

        internal static void SaveAtomically(XmlDocument document, string filePath)
        {
            // Stage on the same volume so publishing never truncates the live file.
            string temporaryPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    document.Save(stream);
                    stream.Flush(true);
                }

                if (File.Exists(filePath))
                {
                    File.Replace(temporaryPath, filePath, filePath + ".bak");
                }
                else
                {
                    File.Move(temporaryPath, filePath);
                }
            }
            finally
            {
                // Preserve the original save error if cleanup also fails.
                try { File.Delete(temporaryPath); }
                catch (IOException ex) { Log.Warn("Could not remove temporary settings file", ex); }
                catch (UnauthorizedAccessException ex) { Log.Warn("Could not remove temporary settings file", ex); }
            }
        }

        public override SettingsPropertyValueCollection GetPropertyValues(SettingsContext context, SettingsPropertyCollection collection)
        {
            // Create new collection of values
            SettingsPropertyValueCollection values = new SettingsPropertyValueCollection();

            // Iterate through the settings to be retrieved
            foreach (SettingsProperty setting in collection)
            {
                SettingsPropertyValue value = new SettingsPropertyValue(setting)
                {
                    IsDirty = true,
                    SerializedValue = GetValue(setting)
                };
                values.Add(value);
            }

            return values;
        }

        public XmlDocument SettingsXML
        {
            get
            {
                if (this.settingsXML == null)
                {
                    try
                    {
                        SettingsFilePath = GetAppSettingsFilePath();
                        this.settingsXML = LoadSettingsDocument(SettingsFilePath);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                        ex is System.Security.SecurityException || ex is XmlException)
                    {
                        Log.Error("Could not load application preferences; the existing files were not replaced with defaults.", ex);
                        throw new ConfigurationErrorsException(
                            "Unable to load application preferences. Check access to the settings file and restore a valid backup if necessary. " +
                            "Existing preferences have not been replaced with defaults. File: " + SettingsFilePath, ex);
                    }
                }

                return this.settingsXML;
            }
        }

        private XmlDocument LoadSettingsDocument(string settingsFile)
        {
            bool primaryMissing = false;
            Exception primaryFailure;
            try
            {
                using (var stream = File.OpenRead(settingsFile))
                {
                    XmlDocument document = ReadSettingsDocument(stream);
                    Log.InfoFormat("Loaded settings from {0}", settingsFile);
                    return document;
                }
            }
            catch (FileNotFoundException ex) { primaryMissing = true; primaryFailure = ex; }
            catch (DirectoryNotFoundException ex) { primaryMissing = true; primaryFailure = ex; }
            catch (XmlException ex) { primaryFailure = ex; }

            if (!primaryMissing) Log.Warn("Application preferences are invalid; checking the backup: " + settingsFile, primaryFailure);
            string backupFile = settingsFile + ".bak";
            byte[] backupBytes;
            try { backupBytes = File.ReadAllBytes(backupFile); }
            catch (FileNotFoundException)
            {
                if (!primaryMissing) throw new XmlException("Application preferences are invalid and no backup exists.", primaryFailure);
                return CreateSettingsDocument();
            }
            catch (DirectoryNotFoundException)
            {
                if (!primaryMissing) throw new XmlException("Application preferences are invalid and no backup exists.", primaryFailure);
                return CreateSettingsDocument();
            }

            XmlDocument recovered;
            try
            {
                using (var stream = new MemoryStream(backupBytes, false))
                    recovered = ReadSettingsDocument(stream);
            }
            catch (XmlException ex)
            {
                Log.Error("The application preference backup is also invalid: " + backupFile, ex);
                throw new XmlException("No valid application preference file or backup could be loaded.", primaryFailure);
            }

            string preservedFile = primaryMissing ? null : settingsFile + ".corrupt." + Guid.NewGuid().ToString("N");
            string temporaryFile = settingsFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporaryFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(backupBytes, 0, backupBytes.Length);
                    stream.Flush(true);
                }

                // Never put the damaged primary into .bak. Restore the exact validated
                // backup bytes and preserve the damaged original under a unique name.
                if (primaryMissing) File.Move(temporaryFile, settingsFile);
                else File.Replace(temporaryFile, settingsFile, preservedFile);
            }
            finally
            {
                try { File.Delete(temporaryFile); }
                catch (IOException ex) { Log.Warn("Could not remove temporary recovery file", ex); }
                catch (UnauthorizedAccessException ex) { Log.Warn("Could not remove temporary recovery file", ex); }
            }

            RecoveryNotification = new SettingsRecoveryNotification(settingsFile, backupFile, preservedFile);
            Log.WarnFormat("Recovered preferences from {0}. Preserved original: {1}", backupFile, preservedFile ?? "primary file was missing");
            return recovered;
        }

        private static XmlDocument ReadSettingsDocument(Stream stream)
        {
            var document = new XmlDocument { XmlResolver = null };
            var readerSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using (XmlReader reader = XmlReader.Create(stream, readerSettings)) document.Load(reader);
            if (document.DocumentElement == null || document.DocumentElement.Name != SettingsRoot ||
                document.DocumentElement.NamespaceURI.Length != 0)
                throw new XmlException("Application preferences must have a Settings root element without a namespace.");
            return document;
        }

        private static XmlDocument CreateSettingsDocument()
        {
            var document = new XmlDocument { XmlResolver = null };
            document.AppendChild(document.CreateXmlDeclaration("1.0", "utf-8", String.Empty));
            document.AppendChild(document.CreateElement(SettingsRoot));
            return document;
        }

        private string GetValue(SettingsProperty setting)
        {
            // A load failure must escape the fallback for individual missing values.
            XmlDocument document = SettingsXML;
            string value = setting.DefaultValue.ToString();

            try
            {
                if (UseRoamingSettings(setting))
                {
                    XmlNode node = document.SelectSingleNode(SettingsRoot + "/" + setting.Name);
                    
                    if (node == null)
                    {
                        // try go by host...backwards compatibility
                        node = document.SelectSingleNode(SettingsRoot + "/" + GetHostName() + "/" + setting.Name);
                    }

                    value = node != null ? (node.InnerText ?? String.Empty) : setting.DefaultValue.ToString();
                }
                else
                {
                    value = document.SelectSingleNode(SettingsRoot + "/" + GetHostName() + "/" + setting.Name).InnerText;
                }
            }
            catch (Exception)
            {
                value = setting.DefaultValue != null ? setting.DefaultValue.ToString() : String.Empty;
            }

            return value;
        }

        private void SetValue(SettingsPropertyValue propVal)
        {
            XmlNode machineNode;
            XmlNode settingNode;

            // Determine if the setting is roaming.
            // If roaming then the value is stored as an element under the root
            // Otherwise it is stored under a machine name node 
            try
            {
                if (UseRoamingSettings(propVal.Property))
                    settingNode = (XmlElement)SettingsXML.SelectSingleNode(SettingsRoot + "/" + propVal.Name);
                else
                    settingNode = (XmlElement)SettingsXML.SelectSingleNode(SettingsRoot + "/" + GetHostName() + "/" + propVal.Name);
            }
            catch (Exception)
            {
                settingNode = null;
            }


            // Check to see if the node exists, if so then set its new value
            if (settingNode != null)
            {
                settingNode.InnerText = propVal.SerializedValue.ToString();
            }
            else
            {
                if (UseRoamingSettings(propVal.Property))
                {
                    // Store the value as an element of the Settings Root Node
                    settingNode = SettingsXML.CreateElement(propVal.Name);
                    settingNode.InnerText = propVal.SerializedValue.ToString();
                    SettingsXML.SelectSingleNode(SettingsRoot).AppendChild(settingNode);
                }
                else
                {
                    // Its machine specific, store as an element of the machine name node,
                    // creating a new machine name node if one doesnt exist.
                    string nodePath = SettingsRoot + "/" + GetHostName();
                    try
                    {
                        machineNode = (XmlElement)SettingsXML.SelectSingleNode(nodePath);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Error selecting node, " + nodePath, ex);
                        machineNode = SettingsXML.CreateElement(GetHostName());
                        SettingsXML.SelectSingleNode(SettingsRoot).AppendChild(machineNode);
                    }

                    if (machineNode == null)
                    {
                        machineNode = SettingsXML.CreateElement(GetHostName());
                        SettingsXML.SelectSingleNode(SettingsRoot).AppendChild(machineNode);
                    }

                    settingNode = SettingsXML.CreateElement(propVal.Name);
                    settingNode.InnerText = propVal.SerializedValue.ToString();
                    machineNode.AppendChild(settingNode);
                }
            }
        }

        private static string GetHostName()
        {
            return Environment.MachineName;
        }

        private bool UseRoamingSettings(SettingsProperty prop)
        {
            if (ForceRoamingSettings || string.IsNullOrEmpty(Environment.MachineName) || Char.IsDigit(Environment.MachineName[0]))
            {
                return true;
            }

            // Determine if the setting is marked as Roaming
            return (from DictionaryEntry de 
                    in prop.Attributes
                    select (Attribute) de.Value).OfType<SettingsManageabilityAttribute>().Any();
        }
    }
}
