using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.IsolatedStorage;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace BuddySDK
{
    internal abstract class IsolatedStorageSettings
    {
        protected abstract IsolatedStorageFile GetIsolatedStorageFile();

        protected abstract string ExecutionBinDir { get; }

        private static System.Threading.ReaderWriterLockSlim readerWriterLock = new ReaderWriterLockSlim();
        private static int timeout = 10000;

        protected virtual IsoStoreFileStream GetFileStream(bool create)
        {
            IsolatedStorageFile isoStore = null;
            try
            {
                isoStore = GetIsolatedStorageFile();
            }
            catch (IsolatedStorageException)
            {
                // isolated storage not available, fall back to file.
                //
            }
            catch (ApplicationException)
            {
                // isolated storage not available, fall back to file.
                //
            }

            IsoStoreFileStream isfs = null;

            if (isoStore != null)
            {
                if (isoStore.FileExists("_buddy") || create)
                {
                    var fs = isoStore.OpenFile("_buddy", FileMode.OpenOrCreate);
                    isfs = new IsoStoreFileStream(isoStore, fs);
                }
            }
            else
            {
                // if we didn't get an iso store file back, use a file in the local dir.
                string path = Path.Combine(ExecutionBinDir, "_buddy");

                if (File.Exists(path) || create)
                {
                    var fs = File.Open(path, FileMode.OpenOrCreate);
                    isfs = new IsoStoreFileStream(null, fs);
                }
            }
            return isfs;
        }

        public virtual IDictionary<string, string> LoadSettings()
        {
            string existing = "";
            try
            {
                readerWriterLock.EnterReadLock();
                var isfs = GetFileStream(false);
                if (isfs != null)
                {
                    isfs.Using((fs) =>
                    {
                        using (var sr = new StreamReader(fs))
                        {
                            existing = sr.ReadToEnd();
                        }
                    });
                }
            }
            finally
            {
                readerWriterLock.ExitReadLock();
            }

            var d = new Dictionary<string, string>();
            var parts = Regex.Match(existing, "(?<key>[\\w\\.]*)=(?<value>.*?);");

            while (parts.Success)
            {
                d[parts.Groups["key"].Value] = parts.Groups["value"].Value;

                parts = parts.NextMatch();
            }
            return d;
        }

        public void SaveSettings(IDictionary<string, string> values)
        {
            Debug.WriteLine(Thread.CurrentThread.ManagedThreadId + " >SaveSettings");
            var sb = new StringBuilder();

            foreach (var kvp in values)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0}={1};", kvp.Key, kvp.Value ?? "");
            }

            try
            {
                readerWriterLock.EnterWriteLock();

                var isfs = GetFileStream(true);
                isfs.Using((fs) =>
                {
                    var sbString = sb.ToString();
                    using (var sw = new StreamWriter(fs))
                    {
                        sw.WriteLine(sbString);
                        fs.SetLength(sbString.Length);
                    }
                });
            }
            finally
            {
                readerWriterLock.ExitWriteLock();
            }
        }

        public void SetUserSetting(string key, string value, DateTime? expires = default(DateTime?))
        {
            if (key == null)
            {
                throw new ArgumentNullException("key");
            }

            // parse it
            var parsed = LoadSettings();
            string encodedValue = PlatformAccess.EncodeUserSetting(value, expires);
            parsed[key] = encodedValue;

            SaveSettings(parsed);
        }

        public string GetUserSetting(string key)
        {
            string result = null;
            var parsed = LoadSettings();

            if (parsed.ContainsKey(key))
            {
                result = PlatformAccess.DecodeUserSetting(parsed[key]);

                if (result == null)
                {
                    ClearUserSetting(key);
                }
            }

            return result;
        }

        public void ClearUserSetting(string key)
        {
            var parsed = LoadSettings();

            if (parsed.ContainsKey(key))
            {
                parsed.Remove(key);
                SaveSettings(parsed);
            }
        }
    }

    //This is NOT a stream, just a wrapper.
    internal class IsoStoreFileStream
    {
        private IsolatedStorageFile isoStore;
        private FileStream fs;

        public IsoStoreFileStream(IsolatedStorageFile isoStore, FileStream fs)
        {
            this.isoStore = isoStore;
            this.fs = fs;
        }

        public void Using(Action<FileStream> action)
        {
            if (isoStore == null)
            {
                using (fs)
                {
                    action(fs);
                }
            }
            else
            {
                using (isoStore)
                using (fs)
                {
                    action(fs);
                }
            }
        }
    }
}