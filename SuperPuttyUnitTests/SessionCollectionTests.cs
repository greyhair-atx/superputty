using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using SuperPutty.Data;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class SessionCollectionTests
    {
        [Test]
        public void CollectionPlaceholderCanBeTheOnlySessionInAFile()
        {
            string directory = Path.Combine(Path.GetTempPath(), "SuperPuttyCollectionTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string childFile = Path.Combine(directory, "Child.xml");
                SessionData.SaveSessionsToFile(new List<SessionData>
                {
                    new SessionData { SessionId = "Server", SessionName = "Server", Host = "server.example.com" }
                }, childFile);

                string parentFile = Path.Combine(directory, "Parent.xml");
                SessionData.SaveSessionsToFile(new List<SessionData>
                {
                    new SessionData { CollectionID = "Imported", CollectionLocation = childFile }
                }, parentFile);

                List<SessionData> loaded = SessionData.LoadSessionsFromFile(parentFile);

                Assert.AreEqual(1, loaded.Count);
                Assert.AreEqual("Imported/Server", loaded[0].SessionId);
                Assert.AreEqual("server.example.com", loaded[0].Host);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void RemoteRelativeScriptAndCollectionLocationsResolveIndependently()
        {
            SessionData session = new SessionData
            {
                SPSLFileName = "scripts/login.spsl",
                CollectionLocation = "collections/child.xml"
            };

            SessionData.ResolveRemoteSessionLocations(
                new[] { session },
                new Uri("https://example.com/config/root.xml"));

            Assert.AreEqual("https://example.com/config/scripts/login.spsl", session.SPSLFileName);
            Assert.AreEqual("https://example.com/config/collections/child.xml", session.CollectionLocation);
        }

        [Test]
        public void RemoteCollectionsRejectPlainHttp()
        {
            List<SessionData> loaded = SessionData.LoadSessionsFromFile("http://example.com/sessions.xml");

            Assert.IsEmpty(loaded);
        }

        [Test]
        public void CircularCollectionsAreRejected()
        {
            string directory = Path.Combine(Path.GetTempPath(), "SuperPuttyCollectionTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string firstFile = Path.Combine(directory, "First.xml");
                string secondFile = Path.Combine(directory, "Second.xml");
                SessionData.SaveSessionsToFile(new List<SessionData>
                {
                    new SessionData { CollectionLocation = secondFile }
                }, firstFile);
                SessionData.SaveSessionsToFile(new List<SessionData>
                {
                    new SessionData { CollectionLocation = firstFile }
                }, secondFile);

                Assert.Throws<InvalidDataException>(() => SessionData.LoadSessionsFromFile(firstFile));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
