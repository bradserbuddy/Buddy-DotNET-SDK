#if DOTNET
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace BuddySDK
{
    internal partial class BuddyClient
    {
        public void RecordNotificationReceived<T>(T args)
        {
        }
    }

    public abstract partial class PlatformAccess {


        public const BuddyClientFlags DefaultFlags = BuddyClientFlags.AllowReinitialize;

        static PlatformAccess CreatePlatformAccess()
        {
            return new DotNetPlatformAccess();
        }
    }

    internal class DotNetPlatformAccess: DotNetPlatformAccessBase
    {
        public override string Platform
        {
            get { return ".NET"; }
        }

        public override string Model
        {
            get { return null; }
        }

        public override string DeviceUniqueId
        {
            get {

                var uniqueId = GetUserSetting("UniqueId");
                if (uniqueId == null)
                {
                    uniqueId = Guid.NewGuid().ToString();
                    SetUserSetting("UniqueId", uniqueId);
                }
                return uniqueId;
            }
        }

        public override string OSVersion
        {
            get {

                 
                    var osVersionProperty = typeof(Environment).GetRuntimeProperty("OSVersion");
                    object osVersion = osVersionProperty.GetValue(null, null);
                    var versionStringProperty = osVersion.GetType().GetRuntimeProperty("VersionString");
                    var versionString = versionStringProperty.GetValue(osVersion, null);
                    return (string)versionString;
                
            }
        }

        public override bool SupportsFlags(BuddyClientFlags flags)
        {
            return (flags & (BuddyClientFlags.AutoCrashReport | BuddyClientFlags.AllowReinitialize)) == flags;
        }

        public override bool IsEmulator
        {
            get {
                return false;
                }
        }

        public override bool IsUiThread
        {
            get
            {
                return !Thread.CurrentThread.IsThreadPoolThread && base.IsUiThread;
            }
        }

        internal override Assembly EntryAssembly
        {
            get { return Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly(); }
        }

        public override string AppVersion
        {
            get {

                if (EntryAssembly != null) {
                    var attr = EntryAssembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
                    return attr.Version;
                }

                return "1.0";
            }
        }

   
        public override ConnectivityLevel ConnectionType
        {
            get {
                return ConnectivityLevel.WiFi;
            }
        }

      
        private class DotNetIsoStore : IsolatedStorageSettings
        {
            protected override IsolatedStorageFile GetIsolatedStorageFile()
            {
                return IsolatedStorageFile.GetStore(IsolatedStorageScope.User | IsolatedStorageScope.Assembly, null, null);
            }

            protected override string CodeBase { get { return PlatformAccess.Current.EntryAssembly.CodeBase; } }
        }

        private IsolatedStorageSettings _settings = new DotNetIsoStore();

        public override void ClearUserSetting(string str)
        {
            _settings.ClearUserSetting(str);
        }

        public override void SetUserSetting(string key, string value, DateTime? expires = null)
        {
            _settings.SetUserSetting(key, value, expires);
        }

        public override string GetUserSetting(string key)
        {
            return _settings.GetUserSetting(key);
        }

        public override string GetConfigSetting(string key)
        {
            return System.Configuration.ConfigurationManager.AppSettings[key];
        }

        protected override void InvokeOnUiThreadCore(Action a)
        {
            var context = SynchronizationContext.Current;

            if (context != null)
            {
                context.Post((s) => { a(); }, null);
            }
            else
            {
                a();
            }
        }
    }


     // default
 
}

#endif
