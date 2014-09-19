using System;
using System.Collections.Generic;

using System.Linq;
using System.Text;
using System.Threading;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;
using BuddySDK.BuddyServiceClient;
using System.Reflection;
using System.Collections;
using Newtonsoft.Json;
using System.Threading.Tasks;
using BuddySDK.Models;

namespace BuddySDK
{
    public class BuddyOptions
    {
        public BuddyClientFlags Flags { get; set; }
        public string InstanceName { get; set; }
        public string AppVersion { get; set; }
        public string DeviceTag { get; set; }
        public string SharedSecret {internal get; set; }
        public BuddyOptions()
        {
            Flags = PlatformAccess.DefaultFlags;
        }

        public BuddyOptions(BuddyClientFlags flags = PlatformAccess.DefaultFlags, string instanceName = null, string appVersion = null)
        {
            Flags = flags;
            InstanceName = instanceName ?? "";
            AppVersion = appVersion;
        }
    }

   

  
    internal partial class BuddyClient : IRestProvider, IBuddyClient
    {



        public event EventHandler<ServiceExceptionEventArgs> ServiceException;
        public event EventHandler<CurrentUserChangedEventArgs> CurrentUserChanged;
        public event EventHandler<ConnectivityLevelChangedArgs> ConnectivityLevelChanged;
        public event EventHandler AuthorizationLevelChanged;
        public event EventHandler AuthorizationNeedsUserLogin;

        private const string GetVerb = "GET";
        private const string PostVerb = "POST";
        private const string PutVerb = "PUT";
        private const string PatchVerb = "PATCH";
        private const string DeleteVerb = "DELETE";

        private bool _gettingToken = false;
        private User _user;
        private static bool _crashReportingSet = false;
     
        private class AppSettings
        {
            public string AppID {get;set;}
            public string AppKey {get;set;}

            public string ServiceUrl { get; set; }
            public string DeviceToken { get; set; }
            public DateTime? DeviceTokenExpires { get; set; }

            public string UserToken { get; set; }
            public DateTime? UserTokenExpires { get; set; }
            public string UserID {get;set;}
            public string LastUserID {get;set;}

            public string DevicePushToken { get; set; }
            
            public BuddyOptions Options { get; set; }

            public AppSettings() {

            }

            public AppSettings(string appId, string appKey, BuddyOptions options) {
                AppID = appId;
                AppKey = appKey;
                Options = options;

                if (appId != null) {
                    Load();
                }
            }

            public void Clear() {
                if (AppID != null) {
                    PlatformAccess.Current.ClearUserSetting (GetSettingsKey());
                    ServiceUrl = null;
                    DeviceToken = null;
                    DeviceTokenExpires = null;
                    LastUserID = null;
                    ClearUser ();
                }
            }

            public void ClearUser() {
                if (AppID != null) {
                    UserToken = null;
                    UserTokenExpires = null;
                    UserID = null;
                    Save ();
                }
            }

            public void Save() {
                if (AppID == null) {
                    return;
                }

                var json = JsonConvert.SerializeObject (this);
                PlatformAccess.Current.SetUserSetting (GetSettingsKey(), json);
            }

            private string GetSettingsKey()
            {
                return AppID + Options.InstanceName;
            }

            public void Load() {
                if (AppID == null)
                    return;

                var json = PlatformAccess.Current.GetUserSetting (GetSettingsKey());

                if (json == null)
                    return;

                try {
                    var settings = JsonConvert.DeserializeObject<AppSettings> (json);

                    // copy over the properties
                    //
                    foreach (var prop in settings.GetType().GetProperties()) {
                        if (prop.Name != "Options") {
                            prop.SetValue(this, prop.GetValue(settings));
                        }
                    }
                }
                catch {
                    // we don't want to have an app not be able to start because settings got corrupted
                }
            }
        }


        /// <summary>
        /// The service we use to call the Buddy backend.
        /// </summary>
        /// 
        private BuddyServiceClientBase _service;

        private static string _WebServiceUrl;
        protected static string WebServiceUrl {
            get {
                return _WebServiceUrl ?? "https://api.buddyplatform.com/";
            }
            set {
                _WebServiceUrl = value;
            }

        }

        /// <summary>
        /// Gets the application ID for this client.
        /// </summary>
        public string AppId { get; protected set; }

        /// <summary>
        /// Gets the application secret key for this client.
        /// </summary>
        public string AppKey { get; protected set; }

        protected string AccessToken
        {
            get
            {
                return GetAccessToken ().Result;

            }
        }

        internal AuthenticationLevel AuthLevel {
            get;
            private set;
        }


        private BuddyGeoLocation _lastLocation;

        /// <summary>
        /// The last location value set for this device.
        /// </summary>
        /// <value>The last location.</value>
        public BuddyGeoLocation LastLocation
        {
            get
            {
                return _lastLocation;
            }
            
            set
            {
                _lastLocation = value;
            }
        }
        
        private AppSettings _appSettings;
        private bool _userInitialized = false;
        private string sharedSecret = null; // We keep it outside of BuddyOptions to avoid serializing it to stable storage
        public BuddyOptions Options { get; set; }

        public BuddyClient(string appid, string appkey, BuddyOptions options = null)
        {
            if (String.IsNullOrEmpty(appid))
                throw new ArgumentException("Can't be null or empty.", "appId");
            if (String.IsNullOrEmpty(appkey))
                throw new ArgumentException("Can't be null or empty.", "AppKey");
            if (options == null)
            {
                options = new BuddyOptions();
            }

            sharedSecret = options.SharedSecret;
            options.SharedSecret = null;

            if (!PlatformAccess.Current.SupportsFlags(options.Flags))
            {
                throw new ArgumentException("Invalid flags for this client type.");
            }

            this.AppId = appid.Trim();
            this.AppKey = appkey.Trim();

            this.Options = options;

            _appSettings = new AppSettings (appid, appkey, options);

            UpdateAccessLevel();

            if (options.Flags.HasFlag (BuddyClientFlags.AutoCrashReport)) {
                InitCrashReporting ();
            }


            PlatformAccess.Current.SetPushToken(_appSettings.DevicePushToken);
            PlatformAccess.Current.PushTokenChanged += (sender, args) =>
            {
                // update the token.
                PlatformAccess.Current.GetPushTokenAsync().ContinueWith((t) =>
                {
                    if (t.Result != _appSettings.DevicePushToken)
                    {
                        _appSettings.DevicePushToken = t.Result;

                    // if we have a device token, send up the new push token.
                    if (_appSettings.DeviceToken != null)
                    {
                        this.UpdateDeviceAsync(_appSettings.DevicePushToken).ContinueWith((t2) =>
                        {
                            _appSettings.Save();
                        });
                    }
                }
                });

            };

            PlatformAccess.Current.NotificationReceived += (s, na) => {
                string id = na.ID;
                if (_appSettings.DeviceToken != null) {
                    PostAsync<bool>(
                        "/notifications/received/" + id,
                        null);
                }
            };
            
        }

        internal class DeviceRegistration
        {
            public string AccessToken { get; set; }
            public string ServiceRoot { get; set; }
            public string ServerSignature { get; set; }
        }
        
        internal async Task<string> GetAccessToken() {

            if (!_gettingToken)
            {
                try
                {
                    _gettingToken = true;

                    if (_appSettings.UserToken != null) {
                        return _appSettings.UserToken;
                    }
                    else if (_appSettings.DeviceToken != null) {
                        return _appSettings.DeviceToken;
                    }

                    _appSettings.DeviceToken = await GetDeviceToken();
                    _appSettings.Save();
                    return _appSettings.DeviceToken;
                }
                finally
                {
                    _gettingToken = false;
                }
            }
            else
            {
                return _appSettings.UserToken ?? _appSettings.DeviceToken;
            }
        }

        private string GenerateServerSig()
        {
            string stringToSign = string.Format("{0}\n", AppKey);
            return PlatformAccess.Current.SignString(sharedSecret,stringToSign);
        }

        private async Task<string> GetDeviceToken()
        {

            var reg = PostAsync<DeviceRegistration> ("/devices",
                          new
                {
                    AppId = AppId,
                    AppKey = AppKey,
                    ApplicationId = PlatformAccess.Current.ApplicationID,
                    Platform = PlatformAccess.Current.Platform,
                    UniqueID = PlatformAccess.Current.DeviceUniqueId,
                    Model = PlatformAccess.Current.Model,
                    OSVersion = PlatformAccess.Current.OSVersion,
                    PushToken = await PlatformAccess.Current.GetPushTokenAsync (),
                    AppVersion = _appSettings.Options.AppVersion ?? PlatformAccess.Current.AppVersion,
                    Tag = _appSettings.Options.DeviceTag
                });

            var dr = await ResultConversionHelper  <DeviceRegistration, DeviceRegistration> (
                reg,
                completed: (r1, r2) => { 
                    if (r2.IsSuccess)
                    {
                        if (sharedSecret != null)
                        {
                            // Check the server Sig
                            string serverSig = GenerateServerSig();
                            if (serverSig != r2.Value.ServerSignature)
                            {
                                ClearCredentials();
                                return;
                            }
                        }
                        if (r2.Value.ServiceRoot != null)
                        {
                            _service.ServiceRoot = r2.Value.ServiceRoot;
                            _appSettings.ServiceUrl = r2.Value.ServiceRoot;
                        }
                    }
                    else if (!r2.IsSuccess){
                        ClearCredentials();
                    }
                });

           
            if (!dr.IsSuccess) {
                return null;
            }

            

            return dr.Value.AccessToken;
        }
               
        public async Task<bool> UpdateDeviceAsync(string devicePushToken = null, bool? isProduction = true)
        {
            var parameters = new Dictionary<string, object>();

            if (devicePushToken != null)
            {
                parameters["pushToken"] = devicePushToken;
            }

            if (isProduction != null)
            {
                parameters["isProduction"] = isProduction.Value;
            }

            if (parameters.Count() == 0)
            {
                return false;
            }

            BuddyResult<IDictionary<string, object>> result = await ResultConversionHelper<IDictionary<string, object>, IDictionary<string, object>> (
                                                                  PatchAsync<IDictionary<string,object>> (
                                                                      "/devices/current",
                                                                      parameters));
            return result.IsSuccess;
        }

      

        private class AuthenticatedUser  {
           
            public static User FromSettings(AppSettings settings) {
                if (settings.UserToken != null) {
                    return new User {
                        ID = settings.UserID
                    };
                }
                return null;
            }
        }


        private User GetUser() {
            if (!_userInitialized) {
                _userInitialized = true;
                if (_user == null && _appSettings.UserID != null && _appSettings.UserToken != null)
                {
                    SetCurrentUser ( AuthenticatedUser.FromSettings(_appSettings), _appSettings.UserToken, _appSettings.UserTokenExpires );

                    // kick off an update
                    //


                    return _user;
                }
            }

           

            return _user;
        }

        public Task<User> GetCurrentUserAsync (bool reload = false)
        {
            if (_appSettings.UserToken == null) {
                _user = null;
                OnAuthorizationFailure (null);
                return
#if WINDOWS_PHONE_7x
                TaskEx
#else
                Task
#endif
                .FromResult(_user);
            }
            else if (_user != null && !reload) {
                return
#if WINDOWS_PHONE_7x
                TaskEx
#else
                Task
#endif
                .FromResult(_user);
            } else {

                var t = GetAsync<User> ("/users/me");
                return t.ContinueWith <User>(t2 => {


                    if (t2.IsFaulted || t2.Result == null || !t2.Result.IsSuccess) {
                        SetCurrentUser(null);

                        this.OnAuthorizationFailure (null);

                        return null;
                    }
                    if (_user == null) {
                        _user = t2.Result.Value;
                        OnCurrentUserChanged(_user, null);
                    }
                    return t2.Result.Value;
                }, TaskContinuationOptions.ExecuteSynchronously);

            }
        }

        private void SetCurrentUser(User value, string accessToken = null, DateTime? accessTokenExpires = null) {

                string priorId = null;
                if (value != null)
                {
                    if (_appSettings.UserToken != accessToken
                        && _appSettings.UserID != value.ID)
                    {
                        _appSettings.UserToken = accessToken;
                        _appSettings.UserTokenExpires = accessTokenExpires;
                        _appSettings.UserID = value.ID;

                        priorId = _appSettings.LastUserID;

                        if (_user == null)
                        {
                            priorId = "";
                        }

                        _appSettings.LastUserID = value.ID;
                        _appSettings.Save();
                    }
                }
                else
                {
                    priorId = _appSettings.LastUserID ?? "";
                    _appSettings.ClearUser ();
                }
                _user = value;

                if (priorId != null) {
                    OnCurrentUserChanged (value, priorId == "" ? null : priorId);
                }

                OnAccessTokenChanged (_appSettings.UserToken, AccessTokenType.User);

        }

        protected virtual void OnCurrentUserChanged (User newUser, string lastUserId)
        {
            User lastUser = null;

            if (lastUserId != null) {
                lastUser = new User { ID = lastUserId };
            }
            if (CurrentUserChanged != null) {
                PlatformAccess.Current.InvokeOnUiThread(() =>
                    CurrentUserChanged(this, new CurrentUserChangedEventArgs(newUser, lastUser))
                );

            }
        }

        private string GetRootUrl() {
            string setting = null;
            try
            {
                 setting = PlatformAccess.Current.GetConfigSetting("RootUrl");
            }
            catch (NotImplementedException)
            {
                //platform doesn't provide config settings
            }
            var userSetting = _appSettings.ServiceUrl;
            return userSetting ?? setting ?? WebServiceUrl;
        }

        private void InitCrashReporting() {

            if (!_crashReportingSet) {

                _crashReportingSet = true;
                DotNetDeltas.UnhandledException += (sender, e) => {
                    var ex = e.Exception as Exception;

                    // need to do this synchronously or the OS won't wait for us.
                    var t = PostAsync<string>(
                        "/devices/current/crashreports",
                            new {
                                stackTrace = ex.ToString(),
                                message = e.Message
                        }).WrapResult<string, bool>((r1) => r1.IsSuccess);
                   
                    // wait up to a second to let it go out
                    t.Wait(TimeSpan.FromSeconds(2));

                };
            }

        }

    
        internal async Task<BuddyResult<T>> HandleServiceResult<T>( BuddyCallResult<T> serviceResult, bool allowThrow = false){
            var result = new BuddyResult<T> ();
            result.RequestID = serviceResult.RequestID;
            if (serviceResult.Error != null) {
                BuddyServiceException buddyException = null;
                switch (serviceResult.StatusCode) {
                case 0: 
                    buddyException = new BuddyNoInternetException (serviceResult.Error);
                    break;
                case 401:
                case 403:
                    buddyException = new BuddyUnauthorizedException (serviceResult.Error, serviceResult.Message, serviceResult.ErrorNumber);
                    break;
                default:
                    buddyException = new BuddySDK.BuddyServiceException (serviceResult.Error, serviceResult.Message, serviceResult.ErrorNumber);
                    break;

                }
                TaskCompletionSource<bool> uiPromise = new TaskCompletionSource<bool> ();
                PlatformAccess.Current.InvokeOnUiThread (() => {

                    var r = false;
                    if (OnServiceException (this, buddyException)) {
                        r = true;
                    }
                    uiPromise.TrySetResult (r);
                });
                if (await uiPromise.Task && allowThrow) {
                    throw buddyException;
                }
                buddyException.StatusCode = serviceResult.StatusCode;
                result.Error = buddyException;
            } else {
                result.Value = serviceResult.Result;
            }
            return result;
        }

        internal IDictionary<string,object> AddLocationToParameters (object parameters){

            var dictionary = BuddyServiceClientBase.ParametersToDictionary(parameters);
            var loc = LastLocation;
            if (!dictionary.ContainsKey("location") && loc != null) {
                dictionary["location"] = loc.ToString();
            }
            return dictionary;
        }

        internal Task<BuddyResult<T2>> ResultConversionHelper<T1, T2>(
            Task<BuddyResult<T1>> result,
            Func<T1, T2> map = null, 
            Action<BuddyResult<T1>, BuddyResult<T2>> completed = null) {

            BuddyResult<T1> r1Result = null;
            Task<BuddyResult<T2>> task;

            if (typeof(T1) == typeof(T2)) {
                task = result as Task<BuddyResult<T2>>;
            } else {
                task = result.ContinueWith<BuddyResult<T2>>(r1 =>
                {
                    r1Result = r1.Result;

                    if (map == null)
                    {
                        map = (t1) =>
                        {
                            return (T2)(object)t1;
                        };
                    }

                    return r1Result.Convert<T2>(map);
                });
            }

            var tcs = new TaskCompletionSource<BuddyResult<T2>>();
            task.ContinueWith(r2 =>
                {
                    if (completed == null)
                    {
                        tcs.SetResult(r2.Result);
                    }
                    else
                    {
                        PlatformAccess.Current.InvokeOnUiThread(() => { completed(r1Result, r2.Result); tcs.SetResult(r2.Result); });
                    }
                });
            return tcs.Task;
        }
     

        private void ClearCredentials(bool clearUser = true, bool clearDevice = true) {

            if (clearDevice) {

                _appSettings.Clear ();
                if (_service != null)
                {
                    _service.ServiceRoot = GetRootUrl();
                }
            }

            if (clearUser) {
                _appSettings.ClearUser ();
            }

            UpdateAccessLevel ();
        }

        private void LoadState() {

            if (_appSettings.DeviceToken != null) {
                OnAccessTokenChanged (_appSettings.DeviceToken, AccessTokenType.Device);
            }
            var id = _appSettings.LastUserID;
            if (_appSettings.UserToken != null && id != null) {
                SetCurrentUser( AuthenticatedUser.FromSettings(_appSettings), _appSettings.UserToken, _appSettings.UserTokenExpires);
            }
        }

        protected virtual async Task<IRemoteMethodProvider> GetService()
        {
            using (await new AsyncLock().LockAsync())
            {
                if (this._service != null) return this._service;

                var root = GetRootUrl();

                this._service = BuddyServiceClientBase.CreateServiceClient(this,root,AppId,sharedSecret);

                this._service.ServiceException += (object sender, ExceptionEventArgs e) =>
                {

                    if (e.Exception is BuddyUnauthorizedException)
                    {
                        ClearCredentials(true, true);
                    }

                };
                return _service;
            }
        }

        protected enum AccessTokenType {
            Device,
            User
        }

        protected virtual void OnAccessTokenChanged(string token, AccessTokenType tokenType, DateTime? expires = null) {

           
            UpdateAccessLevel();
        }

        private ConnectivityLevel? _connectivity;
        public ConnectivityLevel ConnectivityLevel {
            get {
                if (_connectivity == null) {
                    return PlatformAccess.Current.ConnectionType;
                }
                return _connectivity.GetValueOrDefault(ConnectivityLevel.None);
            }
            private set {
                _connectivity = value;
            }
        }


        private async Task CheckConnectivity(TimeSpan waitTime) {
            var r = await GetAsync<string>( "/service/ping",null);

            if (r != null && r.IsSuccess)
            {
                await OnConnectivityChanged (ConnectivityLevel.Connected);
            }
            else
            {
                // wait a bit and try again
                //
                
                DotNetDeltas.Sleep((int)waitTime.TotalMilliseconds);
                await CheckConnectivity(waitTime);
            }
        }


        protected virtual async Task OnConnectivityChanged(ConnectivityLevel level) {
            using (await new AsyncLock().LockAsync())
            {
                if (level == _connectivity)
                {
                    return;
                }

                if (ConnectivityLevelChanged != null)
                {
                    ConnectivityLevelChanged(this, new ConnectivityLevelChangedArgs
                        {
                            ConnectivityLevel = level
                        });
                }

                _connectivity = level;

                switch (level)
                {
                case ConnectivityLevel.None:
                    await CheckConnectivity(TimeSpan.FromSeconds(1));
                    break;
                }
            }
        }
      
        protected bool OnServiceException(BuddyClient client, BuddyServiceException buddyException) {


            // first see if it's an auth failure.
            //
            if (buddyException is BuddyUnauthorizedException) {
                client.OnAuthorizationFailure ((BuddyUnauthorizedException)buddyException);
                return false;
            } else if (buddyException is BuddyNoInternetException) {
                #pragma warning disable 4014
                    OnConnectivityChanged (ConnectivityLevel.None); // We don't care about async here.
                #pragma warning restore 4014
                return false;
            }

            bool result = false;

            if (ServiceException != null) {
                var args = new ServiceExceptionEventArgs (buddyException);
                ServiceException (this, args);
                result = args.ShouldThrow;
            } 
            return result;
        }

        private int _processingAuthFailure = 0;

        internal virtual void OnAuthorizationFailure(BuddyUnauthorizedException exception) {


            var count = System.Threading.Interlocked.Increment (ref _processingAuthFailure);

            if (count > 1) {
                System.Threading.Interlocked.Decrement(ref _processingAuthFailure);
                return;
            }

            lock (this) {

               try {
                    bool showLoginDialog = exception == null;
                    #pragma warning disable 4014
                    if (exception != null) {
                        switch (exception.Error) {

                        case "AuthAppCredentialsInvalid":
                        case "AuthAccessTokenInvalid":
                            ClearCredentials(false, true);
                            break;
                        case "AuthUserAccessTokenRequired":
                            ClearCredentials(true, false);
                            showLoginDialog = true;
                            break;
                        }
                    }
                    #pragma warning restore 4014

                    if (showLoginDialog) {
                        System.Threading.Interlocked.Increment (ref _processingAuthFailure);

                        PlatformAccess.Current.InvokeOnUiThread (() => {

                            if (this.AuthorizationNeedsUserLogin != null) {
                                this.AuthorizationNeedsUserLogin (this, new EventArgs ());
                            }
                             System.Threading.Interlocked.Decrement (ref _processingAuthFailure);

                        });
                    }
                }
                finally {
                    System.Threading.Interlocked.Decrement (ref _processingAuthFailure);
                }
            }
        }

        protected virtual void OnAuthLevelChanged() {
           
            PlatformAccess.Current.InvokeOnUiThread (() => {

                if (this.AuthorizationLevelChanged != null) {
                    this.AuthorizationLevelChanged (this, EventArgs.Empty);
                }
            });
        }

        private void UpdateAccessLevel() {

            var old = AuthLevel;
            AuthenticationLevel authLevel = AuthenticationLevel.None;
            if (_appSettings.DeviceToken != null) authLevel = AuthenticationLevel.Device;
            if (_appSettings.UserToken != null) authLevel = AuthenticationLevel.User;
            AuthLevel = authLevel;

            if (old != authLevel) {
                OnAuthLevelChanged ();
            }

        }

        // User auth.
        public System.Threading.Tasks.Task<BuddyResult<User>> CreateUserAsync(
            string username, string password, 
            string firstName = null, string lastName = null,
            string email = null, User.UserGender? gender = null, 
            DateTime? dateOfBirth = null, string tag = null)
        {

            if (String.IsNullOrEmpty(username))
                throw new ArgumentException("Can't be null or empty.", "username");
            if (password == null)
                throw new ArgumentNullException("password");
            if (dateOfBirth > DateTime.Now)
                throw new ArgumentException("dateOfBirth must be in the past.", "dateOfBirth");

           
            return LoginUserCoreAsync<User>("/users",  new
                {
                    firstName = firstName,
                    lastName = lastName,
                    username = username,
                    password = password,
                    email = email,
                    gender = gender,
					dateOfBirth = dateOfBirth,
                    tag = tag
                });
         
        }

        /// <summary>
        /// Login an existing user with their username and password. Note that this method internally does two web-service calls, and the IAsyncResult object
        /// returned is only valid for the first one.
        /// </summary>
        /// <param name="username">The username of the user. Can't be null or empty.</param>
        /// <param name="password">The password of the user. Can't be null.</param>
        /// <returns>A Task&lt;AuthenticatedUser&gt;that can be used to monitor progress on this call.</returns>
        public Task<BuddyResult<User>> LoginUserAsync(string username, string password)
        {
            return LoginUserCoreAsync<User>("/users/login", new
            {
                Username = username,
                Password = password
                });
        }

        public Task<BuddyResult<SocialNetworkUser>> SocialLoginUserAsync(string identityProviderName, string identityID, string identityAccessToken)
        {
            return LoginUserCoreAsync<SocialNetworkUser>("/users/login/social", new
                    {
                        IdentityProviderName = identityProviderName,
                        IdentityID = identityID,
                        IdentityAccessToken = identityAccessToken
                });
        }


       static T FromDictionary<T>(IDictionary<string,object> dictionary) {

            // This looks hacky, and well, it is.  But we want all the Json converter goodness
            // to happen by magic so we go to JSON then back.  This happens pretty rarely 
            // so perf isn't a major concern.
            //
            return JsonConvert.DeserializeObject<T> (JsonConvert.SerializeObject (dictionary));
        }

        private Task<BuddyResult<T>> LoginUserCoreAsync<T>(string path, object parameters) where T : User
        {
            var t = PostAsync<IDictionary<string,object>>(
                    path,
                    parameters);
                    
            return t.ContinueWith<BuddyResult<T>>(r => {

                // update our current user
                //
                T user = null;
                if(r.Result.IsSuccess && r.Result.Value != null) {

                    // create the user
                    //
                    var dict = r.Result.Value;
                    user = FromDictionary<T>(dict);
                    SetCurrentUser(user, (string)dict["accessToken"], (DateTime?) dict["accessTokenExpires"]);

                }
                return r.Result.Convert<T>((d) => {

                    return user;
                });
            }, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion);


        }

        private async Task<BuddyResult<bool>> LogoutInternal() {

            IDictionary<string,object> dresult = null;

            bool hadUser = _appSettings.UserToken != null;

            var r = await ResultConversionHelper<IDictionary<string,object>, bool>(
                PostAsync<IDictionary<string,object>>(
                    "/users/me/logout",
                    null),
                map: (d) => {
                    dresult = d;
                    return d != null;
                });

            if (r.IsSuccess) {

                this.SetCurrentUser(null);
                ClearCredentials (true, false);
              
                if (dresult != null && dresult.ContainsKey("accessToken")) {
                    var token = dresult ["accessToken"] as string;
                    DateTime? expires = null;
                    if (dresult.ContainsKey("accessTokenExpires")) {
                        object dt = dresult ["accessTokenExpires"];
                        expires =  (DateTime)dt;
                    }
                    _appSettings.DeviceToken = token;
                    _appSettings.Save ();
                    OnAccessTokenChanged(token, AccessTokenType.Device, expires);
                }

                if (hadUser)
                {
                    OnAuthorizationFailure(null);
                }
            }
            return r;
        }

        public Task<BuddyResult<bool>> LogoutUserAsync() {
            return LogoutInternal ();
           
        }

        public Task<BuddyResult<bool>> RequestPasswordResetAsync(string userName, string subject, string body) {
            return PostAsync<bool> (
                "/users/password",
                new {
                    userName = userName,
                    subject = subject,
                    body = body
                });
        }
        
        public Task<BuddyResult<bool>> ResetPasswordAsync(string userName, string resetCode, string newPassword) {
            return PatchAsync<bool> (
                "/users/password",
                new {
                    userName = userName,
                    resetCode = resetCode,
                    newPassword = newPassword
                });
        }
      
        //
        // Metrics
        //
        private class MetricsResult
        {
            public string id { get; set; }
            public bool success { get; set; }
        }

       

        public Task<BuddyResult<Metric>> RecordMetricAsync(string key, IDictionary<string, object> value = null, TimeSpan? timeout = null, DateTime? timeStamp = null)
        {
            int? timeoutInSeconds = null;

            if (timeout != null)
            {
                timeoutInSeconds = (int)timeout.Value.TotalSeconds;
            }

            var t = PostAsync<Metric>(String.Format(CultureInfo.InvariantCulture, "/metrics/events/{0}", Uri.EscapeDataString(key)), new
                {
                    value = value,
                    timeoutInSeconds = timeoutInSeconds,
                    timeStamp = timeStamp
                });

            return t.ContinueWith((r) => {
                if (r.Result.Value != null)
                {
                    r.Result.Value._client = this;
                }
                return r.Result;
            });
        }

        public void SetPushToken(string token) {
            PlatformAccess.Current.SetPushToken (token);
        }

        #region REST

        //TODO Much awesome refactoring and testing
        private Task<BuddyResult<T>> GenericRestCall<T>(string verb, string path, object parameters, bool allowThrow, TaskCompletionSource<BuddyResult<T>> promise)
        {
            GetService()
                .ContinueWith(service =>
                     service.Result.CallMethodAsync<T>(verb, path, AddLocationToParameters(parameters))
                        .ContinueWith(callResult => {
                            HandleServiceResult(callResult.Result, allowThrow)
                                 .ContinueWith(procResult =>
                                 {
                                     if (procResult.IsFaulted)
                                     {
                                         promise.SetException(procResult.Exception);
                                     }
                                     else
                                     {
                                         promise.SetResult(procResult.Result);
                                     }
                                 });
                         })
                ).ConfigureAwait(false);
            return promise.Task;
        }

        public  Task<BuddyResult<T>> GetAsync<T>(string path, object parameters = null){
            return GenericRestCall(GetVerb, path, parameters, false, new TaskCompletionSource<BuddyResult<T>>());
        }

        public Task<BuddyResult<T>> PostAsync<T>(string path, object parameters = null){
            return GenericRestCall(PostVerb, path, parameters, false, new TaskCompletionSource<BuddyResult<T>>());
        }

        public Task<BuddyResult<T>> PatchAsync<T>(string path, object parameters = null){
            return GenericRestCall(PatchVerb, path, parameters, false, new TaskCompletionSource<BuddyResult<T>>());
        }

        public Task<BuddyResult<T>> PutAsync<T>(string path, object parameters = null){
            return GenericRestCall(PutVerb, path, parameters, false, new TaskCompletionSource<BuddyResult<T>>());
        }

        public Task<BuddyResult<T>> DeleteAsync<T>(string path, object parameters = null){
            return GenericRestCall(DeleteVerb, path, parameters, false, new TaskCompletionSource<BuddyResult<T>>());
        }
        #endregion
    }

    internal enum AuthenticationLevel {
        None = 0,
        Device,
        User
    }
}
