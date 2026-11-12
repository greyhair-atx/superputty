using System;
using System.Collections.Generic;
using NUnit.Framework;
using SuperPutty.Data;

namespace SuperPuttyUnitTests
{
    [TestFixture]
    public class SessionPasswordStateTests
    {
        [Test]
        public void ReadingAnImportedPasswordDoesNotChangeTheModelOrRaiseNotifications()
        {
            var session = new SessionData { ExtraArgs = "-v -pw \"imported value\"" };
            int notifications = 0, changes = 0;
            session.OnPropertyChanged += (s, name) => notifications++;
            session.OnPropertyChanging += delegate(SessionData s, string name, object value, ref bool cancel) { changes++; };
            for (int i = 0; i < 20; i++) Assert.AreEqual("imported value", session.Password);
            Assert.AreEqual(0, notifications);
            Assert.AreEqual(0, changes);
            Assert.AreEqual("-v -pw \"imported value\"", session.ExtraArgs);
        }

        [Test]
        public void ExtraArgumentNotificationsSeeTheNewEffectivePassword()
        {
            var session = new SessionData { ExtraArgs = "-pw old" };
            var passwords = new List<string>();
            var notifications = new List<string>();
            session.OnPropertyChanged += (s, name) =>
            {
                notifications.Add(name);
                passwords.Add(s.Password);
            };
            session.ExtraArgs = "-pw new -v";
            Assert.AreEqual("new", session.Password);
            Assert.Contains("ExtraArgs", notifications);
            Assert.Contains("Password", notifications);
            Assert.That(passwords, Is.All.EqualTo("new"));
            session.ExtraArgs = "-v";
            Assert.That(session.Password, Is.Null.Or.Empty);
        }

        [Test]
        public void CanceledArgumentChangesLeaveThePasswordAndNotificationsUntouched()
        {
            var session = new SessionData { ExtraArgs = "-pw original" };
            int notifications = 0;
            session.OnPropertyChanged += (s, name) => notifications++;
            session.OnPropertyChanging += delegate(SessionData s, string name, object value, ref bool cancel)
            {
                if (name == "ExtraArgs") cancel = true;
            };
            session.ExtraArgs = "-pw replacement";
            Assert.AreEqual("original", session.Password);
            Assert.AreEqual("-pw original", session.ExtraArgs);
            Assert.AreEqual(0, notifications);
        }

        [Test]
        public void ExplicitRuntimePasswordTakesPrecedenceUntilCleared()
        {
            var session = new SessionData { ExtraArgs = "-pw imported", Password = "runtime" };
            session.ExtraArgs = "-pw newer";
            Assert.AreEqual("runtime", session.Password);
            session.Password = String.Empty;
            Assert.AreEqual("newer", session.Password);
            session.ExtraArgs = null;
            Assert.That(session.Password, Is.Null.Or.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CloningPreservesWhetherThePasswordWasExplicitOrDerived(bool explicitPassword)
        {
            var source = new SessionData { ExtraArgs = "-pw imported" };
            if (explicitPassword) source.Password = "runtime";
            var clone = (SessionData)source.Clone();
            clone.ExtraArgs = "-pw changed";
            Assert.AreEqual(explicitPassword ? "runtime" : "changed", clone.Password);
            Assert.AreEqual(explicitPassword ? "runtime" : "imported", source.Password);
            Assert.AreEqual("-pw imported", source.ExtraArgs);
        }

        [Test]
        public void CopyingADerivedPasswordDoesNotTurnItIntoAnExplicitPassword()
        {
            var target = new SessionData { Password = "old runtime", ExtraArgs = "-pw old imported" };
            target.CopyFrom(new SessionData { ExtraArgs = "-pw replacement" });
            Assert.AreEqual("replacement", target.Password);
            target.ExtraArgs = "-pw latest";
            Assert.AreEqual("latest", target.Password);
        }
    }
}
