using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Xml;
using log4net;
using SuperPutty.Utils;
using WeifenLuo.WinFormsUI.Docking;

namespace SuperPutty.Data
{
    internal sealed class SessionDockingHistory
    {
        private const int MaximumBytes = 2 * 1024 * 1024;
        private static readonly ILog Log = LogManager.GetLogger(typeof(SessionDockingHistory));
        private readonly Dictionary<string, Workspace> workspaces =
            new Dictionary<string, Workspace>(StringComparer.OrdinalIgnoreCase);

        private sealed class Workspace
        {
            internal Dictionary<string, DockState> Positions;
            internal readonly Dictionary<string, DockState> Changes = NewPositions();
        }

        internal static string FilePath(string folder)
        {
            return Path.Combine(folder, "SessionDocking.xml");
        }

        private static Dictionary<string, DockState> NewPositions()
        {
            return new Dictionary<string, DockState>(StringComparer.OrdinalIgnoreCase);
        }

        internal DockState Get(string folder, string sessionId, DockState fallback)
        {
            if (String.IsNullOrWhiteSpace(folder)) return fallback;
            DockState state;
            return GetWorkspace(folder).Positions.TryGetValue(sessionId, out state) ? state : fallback;
        }

        internal void Remember(string folder, string sessionId, DockState state, DockState fallback)
        {
            if (!IsDockPosition(state) || state == Get(folder, sessionId, fallback)) return;
            Workspace workspace = GetWorkspace(folder);
            workspace.Positions[sessionId] = workspace.Changes[sessionId] = state;
        }

        internal void Save()
        {
            foreach (var pair in workspaces)
            {
                if (pair.Value.Changes.Count == 0) continue;
                try
                {
                    pair.Value.Positions = SaveChanges(pair.Key, pair.Value.Changes);
                    pair.Value.Changes.Clear();
                }
                catch (Exception ex) when (IsStorageFailure(ex))
                {
                    Log.Warn("Could not save session docking history.", ex);
                }
            }
        }

        private Workspace GetWorkspace(string folder)
        {
            string path = Path.GetFullPath(FilePath(folder));
            Workspace workspace;
            if (!workspaces.TryGetValue(path, out workspace))
            {
                try { workspace = new Workspace { Positions = Read(path, out _) }; }
                catch (Exception ex) when (IsStorageFailure(ex))
                {
                    Log.Warn("Could not read session docking history.", ex);
                    workspace = new Workspace { Positions = NewPositions() };
                }
                workspaces.Add(path, workspace);
            }
            return workspace;
        }

        private static Dictionary<string, DockState> Read(string path, out SharedFile.FileVersion version)
        {
            var positions = NewPositions();
            version = new SharedFile.FileVersion { Missing = true };
            byte[] data;
            try { data = SharedFile.Capture(path, MaximumBytes, out version); }
            catch (FileNotFoundException) { return positions; }
            catch (DirectoryNotFoundException) { return positions; }

            using (var reader = XmlReader.Create(new MemoryStream(data), new XmlReaderSettings
            {
                CloseInput = true,
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreWhitespace = true
            }))
            {
                reader.MoveToContent();
                if (reader.Name != "SessionDocking") throw new InvalidDataException("Invalid session docking history.");
                bool empty = reader.IsEmptyElement;
                reader.ReadStartElement("SessionDocking");
                if (!empty)
                {
                    while (reader.IsStartElement("Session"))
                    {
                        string id = reader.GetAttribute("id");
                        DockState state;
                        if (!String.IsNullOrWhiteSpace(id) &&
                            Enum.TryParse(reader.GetAttribute("state"), out state) && IsDockPosition(state))
                            positions[id] = state;
                        reader.Skip();
                    }
                    reader.ReadEndElement();
                }
                while (reader.Read()) { }
            }
            return positions;
        }

        private static Dictionary<string, DockState> SaveChanges(string path, Dictionary<string, DockState> changes)
        {
            using (SharedFile.Lock(path))
            {
                var version = new SharedFile.FileVersion { Missing = true };
                Dictionary<string, DockState> positions;
                string preserved = null;
                try { positions = Read(path, out version); }
                catch (Exception ex) when (ex is InvalidDataException || ex is XmlException)
                {
                    positions = NewPositions();
                    preserved = path + ".corrupt." + Guid.NewGuid().ToString("N");
                }
                foreach (var change in changes) positions[change.Key] = change.Value;

                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    SharedFile.WriteStaged(temporary, stream =>
                    {
                        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Indent = true }))
                        {
                            writer.WriteStartElement("SessionDocking");
                            foreach (var position in positions)
                            {
                                writer.WriteStartElement("Session");
                                writer.WriteAttributeString("id", position.Key);
                                writer.WriteAttributeString("state", position.Value.ToString());
                                writer.WriteEndElement();
                            }
                            writer.WriteEndElement();
                        }
                    }, MaximumBytes);
                    SharedFile.PublishVersion(temporary, path, preserved, version);
                    if (preserved != null) Log.WarnFormat("Preserved damaged docking history at {0}.", preserved);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                return positions;
            }
        }

        internal static bool IsDockPosition(DockState state)
        {
            return Enum.IsDefined(typeof(DockState), state) && state != DockState.Unknown && state != DockState.Hidden;
        }

        private static bool IsStorageFailure(Exception ex)
        {
            return ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException ||
                ex is XmlException || ex is SecurityException;
        }
    }
}
