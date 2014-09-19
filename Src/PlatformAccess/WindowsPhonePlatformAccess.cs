#if WINDOWS_PHONE

using Microsoft.Phone.Notification;
using System;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Reflection;
using System.Windows.Navigation;
using System.Xml.Linq;

namespace BuddySDK
{
    internal partial class BuddyClient
    {
        public void RecordNotificationReceived<T>(T args) 
        {
            var context = args as NavigationContext;
            string value;
            if (context != null && context.QueryString.TryGetValue(PlatformAccess.BuddyPushKey, out value))
            {
                PlatformAccess.Current.OnNotificationReceived(value);
            }
        }

        private HttpNotificationChannel _channel;

        public HttpNotificationChannel PushNotificationChannel
        {
            get
            {
                return _channel;
            }
            set
            {
                if (_channel != value)
                {
                    _channel.ChannelUriUpdated -= ChannelUriUpdated;
                    _channel = null;
                }
                _channel = value;

                if (_channel != null)
                {
                    PlatformAccess.Current.SetPushToken(_channel.ChannelUri.AbsoluteUri);
                    _channel.ChannelUriUpdated += ChannelUriUpdated;
                }
            }
        }

        void ChannelUriUpdated(object sender, NotificationChannelUriEventArgs e)
        {
            PlatformAccess.Current.SetPushToken(e.ChannelUri.AbsoluteUri);
        }
    }

    public abstract partial class PlatformAccess
    {
        public const BuddyClientFlags DefaultFlags = BuddyClientFlags.Default;

        private static WindowsPhonePlatformAccess CreatePlatformAccess()
        {
            return new WindowsPhonePlatformAccess();
        }
    }

    internal class WindowsPhonePlatformAccess : DotNetPlatformAccessBase
    {
        public override string Platform
        {
            get { 
                return "WindowsPhone"; 
            }
        }

        public override string Model
        {
            get
            {
                return Microsoft.Phone.Info.DeviceStatus.DeviceName;
            }
        }

        public override string ApplicationID
        {
            get
            {
                var xml = XElement.Load("WMAppManifest.xml");
                var prodId = (from app in xml.Descendants("App")
                              select app.Attribute("ProductID").Value).FirstOrDefault();
                if (string.IsNullOrEmpty(prodId)) return string.Empty;
                return new Guid(prodId).ToString();
            }
        }

        public override string DeviceUniqueId
        {
            get
            {
                try
                {
                    byte[] id = (byte[])Microsoft.Phone.Info.DeviceExtendedProperties.GetValue("DeviceUniqueId");

                    if (id != null)
                    {
                        return Convert.ToBase64String(id);
                    }
                }
                catch
                {
                    System.Diagnostics.Debug.WriteLine("You must enable the ID_CAP_IDENTITY_DEVICE capability in WMAppManifest.xml to enable retrieval of DeviceExtendedProperties' DeviceUniqueId.");
                }
                return base.DeviceUniqueId;
            }
        }

        internal override Assembly EntryAssembly
        {
            get
            {
                return Assembly.GetCallingAssembly();
            }
        }

        public override string AppVersion
        {
            get {
                return XDocument.Load("WMAppManifest.xml")
                    .Root.Element("App").Attribute("Version").Value;
            }
        }

        public override string OSVersion
        {
            get
            {
                return System.Environment.OSVersion.Version.ToString();
            }
        }

        public override bool IsEmulator
        {
            get
            {
                return Microsoft.Devices.Environment.DeviceType == Microsoft.Devices.DeviceType.Emulator;
            }
        }

        public override ConnectivityLevel ConnectionType
        {
            get
            {
                return ConnectivityLevel.Carrier;
            }
        }

        private class WindowsPhoneIsoStore : IsolatedStorageSettings
        {
            protected override IsolatedStorageFile GetIsolatedStorageFile()
            {
                return IsolatedStorageFile.GetUserStoreForApplication();
            }

            protected override string CodeBase { get { return null; } }
        }

        private IsolatedStorageSettings _settings = new WindowsPhoneIsoStore();

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
            var appSettings = System.IO.IsolatedStorage.IsolatedStorageSettings.ApplicationSettings;

            return appSettings.Contains(key) ? appSettings[key] as string : null;
        }

        public override bool SupportsFlags(BuddyClientFlags flags)
        {
            return (flags & (BuddyClientFlags.AutoCrashReport)) == flags;
        }
    }
}

#if WINDOWS_PHONE_7x

namespace System.Reflection
{
    internal static class CustomAttributeExtensions
    {
        public static T GetCustomAttribute<T>(PropertyInfo pi) where T : Attribute
        {
            return (T)pi.GetCustomAttributes(true).SingleOrDefault(attribute => attribute is T);
        }

        public static T GetCustomAttribute<T>(Type type) where T : Attribute
        {
            return (T)type.GetCustomAttributes(true).SingleOrDefault(attribute => attribute is T);
        }

        public static object GetValue(this PropertyInfo pi, object obj)
        {
            return pi.GetValue(obj, new object[0]);
        }

        public static void SetValue(this PropertyInfo pi, object obj, object value)
        {
            pi.SetValue(obj, value, new object[0]);
        }

        public static Type GetTypeInfo(this Type type)
        {
            return type;
        }
    }
}

namespace System
{
    internal static class EnumExtensions
    {
        // from http://www.sambeauvois.be/blog/2011/08/enum-hasflag-method-extension-for-4-0-framework/
        public static bool HasFlag(this Enum variable, Enum value)
        {
            // check if from the same type.
            if (variable.GetType() != value.GetType())
            {
                throw new ArgumentException("The checked flag is not from the same type as the checked variable.");
            }

            Convert.ToUInt64(value);
            ulong num = Convert.ToUInt64(value);
            ulong num2 = Convert.ToUInt64(variable);

            return (num2 & num) == num;
        }
    }
}
#endif

#endif