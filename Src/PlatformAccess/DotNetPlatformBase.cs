using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BuddySDK
{
    internal abstract class DotNetPlatformAccessBase : PlatformAccess
    {
        public override string Platform
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public override string Model
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public override string DeviceUniqueId
        {
            get
            {

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
            get
            {
                throw new NotImplementedException();
            }
        }

        public override bool IsEmulator
        {
            get
            {
                return false;
            }
        }

        public override string ApplicationID
        {
            get
            {
                return EntryAssembly.FullName;

            }
        }

        
        public override ConnectivityLevel ConnectionType
        {
            get
            {
                return ConnectivityLevel.Carrier;
            }
        }


        public override string GetConfigSetting(string key)
        {
            throw new NotImplementedException();
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
}
