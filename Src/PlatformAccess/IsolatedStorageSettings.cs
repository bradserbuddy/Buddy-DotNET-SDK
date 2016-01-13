using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.IsolatedStorage;
using System.Text;
using System.Text.RegularExpressions;

namespace BuddySDK
{
    internal abstract class IsolatedStorageSettings
    {
        protected abstract IsolatedStorageFile GetIsolatedStorageFile();

        protected abstract string ExecutionBinDir { get; }

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
            var sb = new StringBuilder();

            foreach (var kvp in values)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0}={1};", kvp.Key, kvp.Value ?? "");
            }

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

        public void SetUserSetting(string key, string value, DateTime? expires = default(DateTime?))
        {
            if (key == null) throw new ArgumentNullException("key");

            // parse it
            var parsed = LoadSettings();
            string encodedValue = PlatformAccess.EncodeUserSetting(value, expires);
            parsed[key] = encodedValue;

            SaveSettings(parsed);
        }

        public string GetUserSetting(string key)
        {
            var parsed = LoadSettings();

            if (parsed.ContainsKey(key))
            {
                var value = PlatformAccess.DecodeUserSetting((string)parsed[key]);

                if (value == null)
                {
                    ClearUserSetting(key);
                }

                return value;
            }

            return null;
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

    internal class IsoStoreFileStream
    {
        private IsolatedStorageFile isoStore;
        private FileStream fs;
        private static object lockObject = new object();

        public IsoStoreFileStream(IsolatedStorageFile isoStore, FileStream fs)
        {
            this.isoStore = isoStore;
            this.fs = fs;
        }

        public void Using(Action<FileStream> action)
        {
            lock (lockObject)
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
}