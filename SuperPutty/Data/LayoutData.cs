using System;
using System.IO;

namespace SuperPutty.Data
{
    public class LayoutData
    {
        public const string AutoRestore = "<Auto Restore>";
        public const string AutoRestoreLayoutFileName = "AutoRestoreLayout.XML";

        public LayoutData(string filePath)
        {
            this.FilePath = filePath;
            this.Name = Path.GetFileNameWithoutExtension(filePath);
        }

        internal static bool TryValidateName(string name, out string error)
        {
            error = "Enter a valid layout filename without a path.";
            if (string.IsNullOrWhiteSpace(name) || name.Length > 251 || name != name.Trim()
                || name.EndsWith(".", StringComparison.Ordinal) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || Path.IsPathRooted(name))
                return false;
            string stem = name.Split('.')[0].ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL"
                || stem == "CONIN$" || stem == "CONOUT$"
                || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT"))
                    && "123456789¹²³".IndexOf(stem[3]) >= 0))
                return false;
            error = null;
            return true;
        }

        public string Name { get; set; }
        public string FilePath { get; set; }

        public bool IsReadOnly { get; set; }

        public bool IsDefault { get { return this.Name == SuperPuTTY.Settings.DefaultLayoutName; } }

        public override string ToString()
        {
            return IsDefault ? String.Format("{0} (default)", this.Name) : this.Name;
        }
    }

    public class LayoutChangedEventArgs : EventArgs
    {
        public LayoutData New { get; set; }
        public LayoutData Old { get; set; }
        public bool IsNewLayoutAlreadyActive { get; set; }
    }
}
