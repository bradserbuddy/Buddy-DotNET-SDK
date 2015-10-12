using System.Net;


#if __IOS__
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CoreLocation;
using CoreFoundation;
using UIKit;
using Foundation;
using SystemConfiguration;


namespace BuddySDK
{
    internal partial class BuddyClient {

        public void RecordNotificationReceived<T>(T args) {
            var message = args as UILocalNotification;

            NSObject id;
            if (message != null && message.UserInfo.TryGetValue (new NSString(PlatformAccess.BuddyPushKey), out id)) {
                PlatformAccess.Current.OnNotificationReceived (id.ToString());
            }
        }
    }

    public partial class Buddy {
        public static void RecordNotificationReceived(UILocalNotification message) {

            CurrentInstance.RecordNotificationReceived (message);
        }
    }

	public abstract partial class PlatformAccess {

        public const BuddyClientFlags DefaultFlags = BuddyClientFlags.AutoCrashReport;

        internal static PlatformAccess CreatePlatformAccess() {
            return new IosPlatformAccess ();
        }
    }

    internal class IosPlatformAccess : PlatformAccess
    {
        private NSObject invoker = new NSObject();

        public override string GetConfigSetting(string key)
        {
            var val = NSBundle.MainBundle.ObjectForInfoDictionary(key);

            if (val != null)
            {
                return val.ToString();
            }

            return null;
        }

        public override void SetUserSetting(string key, string value, DateTime? expires = default(DateTime?))
        {
            string encodedValue = EncodeUserSetting(value, expires);

            NSUserDefaults.StandardUserDefaults.SetString(encodedValue, key);
        }

        public override void ClearUserSetting(string key)
        {
            NSUserDefaults.StandardUserDefaults.RemoveObject(key);
        }

        public override string GetUserSetting(string key)
        {
            var value = NSUserDefaults.StandardUserDefaults.StringForKey(key);
            if (value == null)
            {
                return null;
            }

            var decodedValue = DecodeUserSetting(value.ToString());

            if (decodedValue == null)
            {
                ClearUserSetting(key);
            }

            return decodedValue;
        }

        public override string Platform
        {
            get
            {
                return "iOS";
            }
        }

        public override string Model
        {
            get
            {
                // TODO: see code at http://pastebin.com/FJfpGRbQ
                return "iPhone";
            }
        }

        public override string DeviceUniqueId
        {
            get
            {
                return UIDevice.CurrentDevice.IdentifierForVendor.AsString();
            }
        }

        public override string OSVersion
        {
            get
            {
                return "1.0";
            }
        }


        public override bool IsEmulator
        {
            get
            {
                return ObjCRuntime.Runtime.Arch == ObjCRuntime.Arch.SIMULATOR;
            }
        }

        public override string ApplicationID
        {
            get
            {
                return NSBundle.MainBundle.BundleIdentifier;
            }

        }

        public override string AppVersion
        {
            get
            {
                NSDictionary infoDictionary = NSBundle.MainBundle.InfoDictionary;

                var val = infoDictionary[new NSString("CFBundleShortVersionString")];
                if (val != null)
                {
                    return val.ToString();
                }
                return null;

            }
        }

        public override bool SupportsFlags(BuddyClientFlags flags) {
			return (flags & (BuddyClientFlags.AutoCrashReport | BuddyClientFlags.AllowReinitialize)) == flags;
        }


        protected override void OnShowActivity(bool show)
        {
            UIApplication.SharedApplication.NetworkActivityIndicatorVisible = show;
        }


        protected override void InvokeOnUiThreadCore(Action a)
        {
            //NSAction has been replaced with standard action
            invoker.BeginInvokeOnMainThread(a);

        }


        // FROM: https://github.com/xamarin/monotouch-samples/tree/master/ReachabilitySample
        // Licence: Apache 2.0
        // Author: Miguel de Icaza
        //
        public static class Reachability
        {
            public enum NetworkStatus
            {
                NotReachable,
                ReachableViaCarrierDataNetwork,
                ReachableViaWiFiNetwork
            }

            public static string HostName = "www.buddy.com";

            public static bool IsReachableWithoutRequiringConnection(NetworkReachabilityFlags flags)
            {
                // Is it reachable with the current network configuration?
                bool isReachable = (flags & NetworkReachabilityFlags.Reachable) != 0;

                // Do we need a connection to reach it?
                bool noConnectionRequired = (flags & NetworkReachabilityFlags.ConnectionRequired) == 0;

                // Since the network stack will automatically try to get the WAN up,
                // probe that
                if ((flags & NetworkReachabilityFlags.IsWWAN) != 0)
                    noConnectionRequired = true;

                return isReachable && noConnectionRequired;
            }

            // Is the host reachable with the current network configuration
            public static bool IsHostReachable(string host)
            {
                if (host == null || host.Length == 0)
                    return false;

                using (var r = new NetworkReachability(host))
                {
                    NetworkReachabilityFlags flags;

                    if (r.TryGetFlags(out flags))
                    {
                        return IsReachableWithoutRequiringConnection(flags);
                    }
                }
                return false;
            }

            // 
            // Raised every time there is an interesting reachable event, 
            // we do not even pass the info as to what changed, and 
            // we lump all three status we probe into one
            //
            public static event EventHandler ReachabilityChanged;

            static void OnChange(NetworkReachabilityFlags flags)
            {
                var h = ReachabilityChanged;
                if (h != null)
                    h(null, EventArgs.Empty);
            }

            //
            // Returns true if it is possible to reach the AdHoc WiFi network
            // and optionally provides extra network reachability flags as the
            // out parameter
            //
            static NetworkReachability adHocWiFiNetworkReachability;
            public static bool IsAdHocWiFiNetworkAvailable(out NetworkReachabilityFlags flags)
            {
                if (adHocWiFiNetworkReachability == null)
                {
                    adHocWiFiNetworkReachability = new NetworkReachability(new IPAddress(new byte[] { 169, 254, 0, 0 }));
                    adHocWiFiNetworkReachability.SetNotification(OnChange);
                    adHocWiFiNetworkReachability.Schedule(CFRunLoop.Current, CFRunLoop.ModeDefault);
                }

                if (!adHocWiFiNetworkReachability.TryGetFlags(out flags))
                    return false;

                return IsReachableWithoutRequiringConnection(flags);
            }

            static NetworkReachability defaultRouteReachability;
            static bool IsNetworkAvailable(out NetworkReachabilityFlags flags)
            {
                if (defaultRouteReachability == null)
                {
                    defaultRouteReachability = new NetworkReachability(new IPAddress(0));
                    defaultRouteReachability.SetNotification(OnChange);
                    defaultRouteReachability.Schedule(CFRunLoop.Current, CFRunLoop.ModeDefault);
                }
                if (!defaultRouteReachability.TryGetFlags(out flags))
                    return false;
                return IsReachableWithoutRequiringConnection(flags);
            }

            static NetworkReachability remoteHostReachability;
            public static NetworkStatus RemoteHostStatus()
            {
                NetworkReachabilityFlags flags;
                bool reachable;

                if (remoteHostReachability == null)
                {
                    remoteHostReachability = new NetworkReachability(HostName);

                    // Need to probe before we queue, or we wont get any meaningful values
                    // this only happens when you create NetworkReachability from a hostname
                    reachable = remoteHostReachability.TryGetFlags(out flags);

                    remoteHostReachability.SetNotification(OnChange);
                    remoteHostReachability.Schedule(CFRunLoop.Current, CFRunLoop.ModeDefault);
                }
                else
                    reachable = remoteHostReachability.TryGetFlags(out flags);

                if (!reachable)
                    return NetworkStatus.NotReachable;

                if (!IsReachableWithoutRequiringConnection(flags))
                    return NetworkStatus.NotReachable;

                if ((flags & NetworkReachabilityFlags.IsWWAN) != 0)
                    return NetworkStatus.ReachableViaCarrierDataNetwork;

                return NetworkStatus.ReachableViaWiFiNetwork;
            }

            public static NetworkStatus InternetConnectionStatus()
            {
                NetworkReachabilityFlags flags;
                bool defaultNetworkAvailable = IsNetworkAvailable(out flags);
                if (defaultNetworkAvailable)
                {
                    if ((flags & NetworkReachabilityFlags.IsDirect) != 0)
                        return NetworkStatus.NotReachable;
                }
                else if ((flags & NetworkReachabilityFlags.IsWWAN) != 0)
                    return NetworkStatus.ReachableViaCarrierDataNetwork;
                else if (flags == 0)
                    return NetworkStatus.NotReachable;
                return NetworkStatus.ReachableViaWiFiNetwork;
            }

            public static NetworkStatus LocalWifiConnectionStatus()
            {
                NetworkReachabilityFlags flags;
                if (IsAdHocWiFiNetworkAvailable(out flags))
                {
                    if ((flags & NetworkReachabilityFlags.IsDirect) != 0)
                        return NetworkStatus.ReachableViaWiFiNetwork;
                }
                return NetworkStatus.NotReachable;
            }
        }

        public override ConnectivityLevel ConnectionType
        {
            get
            {
                switch (Reachability.InternetConnectionStatus())
                {
                    case Reachability.NetworkStatus.NotReachable:
                        return ConnectivityLevel.None;
                    case Reachability.NetworkStatus.ReachableViaCarrierDataNetwork:
                        return ConnectivityLevel.Carrier;
                    case Reachability.NetworkStatus.ReachableViaWiFiNetwork:
                        return ConnectivityLevel.WiFi;
                    default:
                        throw new NotSupportedException();
                }
            }
        }

       

    }

    public static class IosExtensions
    {


        public static BuddyGeoLocation ToBuddyGeoLocation(this CoreLocation.CLLocation loc)
        {
            return new BuddyGeoLocation(loc.Coordinate.Latitude, loc.Coordinate.Longitude);
        }

        public static CLLocation ToCLLocation(this BuddyGeoLocation loc)
        {
            var clLoc = new CLLocation(loc.Latitude, loc.Longitude);
            return clLoc;
        }
    }
}



#endif