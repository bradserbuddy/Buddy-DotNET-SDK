#if NETFX_CORE
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.Core;
using Windows.ApplicationModel.Store;
using Windows.Devices.Enumeration.Pnp;
using Windows.Networking.PushNotifications;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System.Profile;
using Windows.UI.Xaml;

namespace BuddySDK
{
    internal partial class BuddyClient
    {
        public void RecordNotificationReceived<T>(T args)
        {
            var launchArgs = args as LaunchActivatedEventArgs;

            var id = launchArgs.Arguments;

            if (!String.IsNullOrEmpty(id))
            {
                var match = Regex.Match(id, PlatformAccess.BuddyPushKey + "=(?<id>[^;]+)");
                if (match.Success)
                {
                    PlatformAccess.Current.OnNotificationReceived(match.Groups["id"].Value);
                }
            }
        }

        private PushNotificationChannel _channel;

        public PushNotificationChannel PushNotificationChannel {
            get {
                return _channel;
            }
            set {
                if (_channel != value)
                {
                    _channel = null;
                }
                _channel = value;

                if (_channel != null)
                {
                    PlatformAccess.Current.SetPushToken(_channel.Uri);

                    // var timeUntilExpires = _channel.ExpirationTime.UtcDateTime.Subtract(DateTime.UtcNow);
                }
            }
        }

       
    }

    public abstract partial class PlatformAccess {
        public const BuddyClientFlags DefaultFlags = BuddyClientFlags.Default;

        

        private static PlatformAccess CreatePlatformAccess()
        {
            return new WindowsPlatformAccess();
        }
    }

    internal class WindowsPlatformAccess : DotNetPlatformAccessBase
    {

        public override string Platform
        {
            get { 
#if WINDOWS_PHONE_APP
                return "WindowsPhone";
#else
                return "WindowsStore";
#endif
            }
        }

        public override string Model
        {
            get
            {
                var t = CSharpAnalytics.WindowsStoreSystemInformation.GetDeviceModelAsync();

                t.Wait();

                return t.Result;
            }
        }

        public override string OSVersion
        {
            get
            {
                var t = CSharpAnalytics.WindowsStoreSystemInformation.GetWindowsVersionAsync();
                t.Wait();
                return t.Result;
            }
        }

        public override string DeviceUniqueId
        {
            get
            {
                var packageToken = HardwareIdentification.GetPackageSpecificToken(null);

                var dataReader = DataReader.FromBuffer(packageToken.Id);

                var bytes = new byte[packageToken.Id.Length];
                dataReader.ReadBytes(bytes);

                var id = BitConverter.ToString(bytes).Replace("-", "");

                return id;
            }
        }

        public override string ApplicationID
        {
            get
            {
                return CurrentApp.AppId.ToString();
            }
        }

        public override string AppVersion
        {
            get
            {
                var pv = Windows.ApplicationModel.Package.Current.Id.Version;

                return new Version(pv.Major, pv.Minor, pv.Build, pv.Revision).ToString();
            }
        }

        internal override Assembly EntryAssembly
        {
            get {
                return Application.Current.GetType().GetTypeInfo().Assembly;
            }
        }

        public override bool SupportsFlags(BuddyClientFlags flags)
        {
            return (flags & (BuddyClientFlags.AutoCrashReport)) == flags;
        }

        private void EnsureSettings(string key)
        {
            Windows.Storage.ApplicationData.Current.LocalSettings.CreateContainer(key, ApplicationDataCreateDisposition.Always);        
        }

        public override string GetUserSetting(string key)
        {
            EnsureSettings(key);
            var val = Windows.Storage.ApplicationData.Current.LocalSettings.Values[key] as string;
            if (val != null)
            {
                var decoded = PlatformAccess.DecodeUserSetting(val);

                if (decoded != null)
                {
                    val = decoded;
                }
            }

            return val;
        }

        public override void SetUserSetting(string key, string value, DateTime? expires = default(DateTime?))
        {
            EnsureSettings(key);
            Windows.Storage.ApplicationData.Current.LocalSettings.Values[key] = PlatformAccess.EncodeUserSetting(value, expires) ;
        }

        public override void ClearUserSetting(string str)
        {
            EnsureSettings(str);
            Windows.Storage.ApplicationData.Current.LocalSettings.Values.Remove(str);
        }

        public override string GetConfigSetting(string key)
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;

            // Create a simple setting

            return localSettings.Values[key] as string;
        }

        protected override void InvokeOnUiThreadCore(Action a)
        {
            CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => a());
        }
    }

    internal static class DotNetDeltas
    {
        public static PropertyInfo GetProperty(this System.Type t, string name)
        {
            return t.GetRuntimeProperty(name);
        }
        public static ConstructorInfo GetConstructor(this System.Type t, params Type[] paramTypes)
        {
            return t.GetTypeInfo().DeclaredConstructors.Where(c => c.GetParameters().Count().Equals(paramTypes.Count()))
                .FirstOrDefault(c => c.GetParameters().All(p => p.ParameterType.IsAssignableFrom(paramTypes.ElementAt(p.Position))));
        }
        public static T GetCustomAttribute<T>(this System.Reflection.PropertyInfo pi) where T : System.Attribute
        {
            return System.Reflection.CustomAttributeExtensions.GetCustomAttribute<T>(pi);
        }
        public static T GetCustomAttribute<T>(this System.Type t) where T : System.Attribute
        {
            return System.Reflection.CustomAttributeExtensions.GetCustomAttribute<T>(t.GetTypeInfo());
        }

        public static int CurrentThreadId
        {
            get
            {
                return Environment.CurrentManagedThreadId;
            }
        }

        public static IEnumerable<PropertyInfo> GetProperties(this Type t)
        {
            return t.GetRuntimeProperties();
        }

        public static bool IsAssignableFrom(this Type t, Type other)
        {
            return t.GetTypeInfo().IsAssignableFrom(other.GetTypeInfo());
        }

        public static bool IsInstanceOfType(this Type t, object obj)
        {
            if (obj == null) return false;
            return IsAssignableFrom(t, obj.GetType());
        }

        public static void Sleep(int ms)
        {
            Task.Delay(ms).Wait();
        }

        public static StringComparer InvariantComparer(bool ignoreCase = false)
        {
            if (ignoreCase)
            {
                return StringComparer.OrdinalIgnoreCase;
            }
            else
            {
                return StringComparer.Ordinal;
            }
        }

        public class ExceptionEventArgs {
            public Exception Exception {get;set;}
            public string Message { get; set; }
            public bool IsHandled { get; set; }
        }

        public static event EventHandler<ExceptionEventArgs> UnhandledException;

        static DotNetDeltas()
        {
            Application.Current.UnhandledException += (s, args) =>
            {
                var a = new ExceptionEventArgs { 
                    Exception = args.Exception, 
                    Message = args.Message 
                };
                if (UnhandledException != null)
                {
                    UnhandledException(null, a);
                }
                args.Handled = a.IsHandled;
            };
        }

        public static string SignString(string key, string message)
        {
            MacAlgorithmProvider macAlgorithmProvider = MacAlgorithmProvider.OpenAlgorithm("HMAC_SHA256");
            var binaryMessage = CryptographicBuffer.ConvertStringToBinary(message, BinaryStringEncoding.Utf8);
            var binaryKeyMaterial = CryptographicBuffer.ConvertStringToBinary(key, BinaryStringEncoding.Utf8);
            var hmacKey = macAlgorithmProvider.CreateKey(binaryKeyMaterial);
            var binarySignedMessage = CryptographicEngine.Sign(hmacKey, binaryMessage);
            var signedMessage = CryptographicBuffer.EncodeToHexString(binarySignedMessage);
            return signedMessage;
        }
    }

    // Copyright (c) Attack Pattern LLC.  All rights reserved.
    // Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License. 
    // You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0

    namespace CSharpAnalytics
    {
        /// <summary>
        /// Obtain system information not conveniently exposed by WinRT APIs.
        /// </summary>
        /// <remarks>
        /// Microsoft doesn't really want you getting this information and makes it difficult.
        /// The techniques used here are not bullet proof but are good enough for analytics.
        /// Do not use these methods or techniques for anything more important than that.
        /// (Note that this class was also published as SystemInfoEstimate on our blog)
        /// </remarks>
        public static class WindowsStoreSystemInformation
        {
            private const string ModelNameKey = "System.Devices.ModelName";
            private const string ManufacturerKey = "System.Devices.Manufacturer";
            private const string DisplayPrimaryCategoryKey = "{78C34FC8-104A-4ACA-9EA4-524D52996E57},97";
            private const string DeviceDriverKey = "{A8B865DD-2E3D-4094-AD97-E593A70C75D6}";
            private const string DeviceDriverVersionKey = DeviceDriverKey + ",3";
            private const string DeviceDriverProviderKey = DeviceDriverKey + ",9";
            private const string RootContainer = "{00000000-0000-0000-FFFF-FFFFFFFFFFFF}";
            private const string RootContainerQuery = "System.Devices.ContainerId:=\"" + RootContainer + "\"";

            

            /// <summary>
            /// Get the name of the manufacturer of this computer.
            /// </summary>
            /// <example>Microsoft Corporation</example>
            /// <returns>The name of the manufacturer of this computer.</returns>
            public static async Task<string> GetDeviceManufacturerAsync()
            {
                var rootContainer = await PnpObject.CreateFromIdAsync(PnpObjectType.DeviceContainer, RootContainer, new[] { ManufacturerKey });
                return (string)rootContainer.Properties[ManufacturerKey];
            }

            /// <summary>
            /// Get the name of the model of this computer.
            /// </summary>
            /// <example>Surface with Windows 8</example>
            /// <returns>The name of the model of this computer.</returns>
            public static async Task<string> GetDeviceModelAsync()
            {
                var rootContainer = await PnpObject.CreateFromIdAsync(PnpObjectType.DeviceContainer, RootContainer, new[] { ModelNameKey });
                return (string)rootContainer.Properties[ModelNameKey];
            }

            /// <summary>
            /// Get the device category this computer belongs to.
            /// </summary>
            /// <example>Computer.Desktop, Computer.Tablet</example>
            /// <returns>The category of this device.</returns>
            public static async Task<string> GetDeviceCategoryAsync()
            {
                var rootContainer = await PnpObject.CreateFromIdAsync(PnpObjectType.DeviceContainer, RootContainer, new[] { DisplayPrimaryCategoryKey });
                return (string)rootContainer.Properties[DisplayPrimaryCategoryKey];
            }

            /// <summary>
            /// Get the version of Windows for this computer.
            /// </summary>
            /// <example>6.2</example>
            /// <returns>Version number of Windows running on this computer.</returns>
            public static async Task<string> GetWindowsVersionAsync()
            {
                // There is no good place to get this so we're going to use the most popular
                // Microsoft driver version number from the device tree.
                var requestedProperties = new[] { DeviceDriverVersionKey, DeviceDriverProviderKey };

                var microsoftVersionedDevices = (await PnpObject.FindAllAsync(PnpObjectType.Device, requestedProperties, RootContainerQuery))
                    .Select(d => new { Provider = (string)d.Properties.GetValueOrDefault(DeviceDriverProviderKey),
                                        Version = (string)d.Properties.GetValueOrDefault(DeviceDriverVersionKey) })
                    .Where(d => d.Provider == "Microsoft" && d.Version != null)
                    .ToList();

                var versionNumbers = microsoftVersionedDevices
                    .GroupBy(d => d.Version.Substring(0, d.Version.IndexOf('.', d.Version.IndexOf('.') + 1)))
                    .OrderByDescending(d => d.Count())
                    .ToList();

                var confidence = (versionNumbers[0].Count() * 100 / microsoftVersionedDevices.Count);
                return versionNumbers.Count > 0 ? versionNumbers[0].Key : "";
            }

            static TValue GetValueOrDefault<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dictionary, TKey key)
            {
                TValue value;
                return dictionary.TryGetValue(key, out value) ? value : default(TValue);
            }

            [StructLayout(LayoutKind.Sequential)]
            struct _SYSTEM_INFO
            {
                public ushort wProcessorArchitecture;
                public ushort wReserved;
                public uint dwPageSize;
                public IntPtr lpMinimumApplicationAddress;
                public IntPtr lpMaximumApplicationAddress;
                public UIntPtr dwActiveProcessorMask;
                public uint dwNumberOfProcessors;
                public uint dwProcessorType;
                public uint dwAllocationGranularity;
                public ushort wProcessorLevel;
                public ushort wProcessorRevision;
            };
        }

        public enum ProcessorArchitecture : ushort
        {
            INTEL = 0,
            MIPS = 1,
            ALPHA = 2,
            PPC = 3,
            SHX = 4,
            ARM = 5,
            IA64 = 6,
            ALPHA64 = 7,
            MSIL = 8,
            AMD64 = 9,
            IA32_ON_WIN64 = 10,
            UNKNOWN = 0xFFFF
        }
    }
}
#else

using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace BuddySDK
{
internal static class DotNetDeltas
{
    public static T GetCustomAttribute<T>(this System.Reflection.PropertyInfo pi) where T : System.Attribute
    {
        return System.Reflection.CustomAttributeExtensions.GetCustomAttribute<T>(pi);
    }

    public static T GetCustomAttribute<T>(this System.Type t) where T : System.Attribute
    {
        return System.Reflection.CustomAttributeExtensions.GetCustomAttribute<T>(t);
    }

    public static System.Collections.Generic.IEnumerable<PropertyInfo> GetProperties(this System.Type t)
    {
        return t.GetProperties();
    }

    public static bool IsAssignableFrom(this System.Type t, System.Type other)
    {
        return t.IsAssignableFrom(other.GetTypeInfo());
    }

    public static bool IsInstanceOfType(this System.Type t, object obj)
    {
        return t.IsInstanceOfType(obj);
    }

    public static void Sleep(int ms)
    {
        System.Threading.Thread.Sleep(ms);
    }

    public static int CurrentThreadId
    {
        get
        {
            return System.Threading.Thread.CurrentThread.ManagedThreadId;
        }
    }

    public static System.StringComparer InvariantComparer(bool ignoreCase = false)
    {
        if (ignoreCase)
        {
            return System.StringComparer.InvariantCultureIgnoreCase;
        }
        else
        {
            return System.StringComparer.InvariantCulture;
        }
    }

    public class ExceptionEventArgs : System.EventArgs
    {
        public System.Exception Exception { get; set; }
        public string Message { get; set; }
        public bool IsHandled { get; set; }
    }

    public static event System.EventHandler<ExceptionEventArgs> UnhandledException;

    static DotNetDeltas()
    {
        System.AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var a = new ExceptionEventArgs
            {
                Exception = args.ExceptionObject as System.Exception
            };

            if (UnhandledException != null)
            {
                UnhandledException(null, a);
            }

        };
    }

    public static string SignString(string key, string stringToSign)
    {
        using (var hasher = new HMACSHA256(Encoding.UTF8.GetBytes(key)))
        {
            return BuddyUtils.ToHex(hasher.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
        }
    }
}
}

#endif
