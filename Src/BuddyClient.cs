using BuddySDK.BuddyServiceClient;
using BuddySDK.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


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

        private User _user;
        private static bool _crashReportingSet = false;

        internal class AppSettings
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
                return _WebServiceUrl ?? "https://api.buddyplatform.com";
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

            PlatformAccess.Current.SetPushToken (_appSettings.DevicePushToken);
            PlatformAccess.Current.PushTokenChanged += this.PushTokenChanged;

            PlatformAccess.Current.NotificationReceived += (s, na) => {
                string id = na.ID;
                if (_appSettings.DeviceToken != null) {
                    PostAsync<bool>(
                        "/notifications/received/" + id,
                        null);
                }
            };        
        }

        private async void PushTokenChanged(object sender, EventArgs args)
        {
            var pushToken = PlatformAccess.Current.GetPushToken();

            if (pushToken != null && pushToken != _appSettings.DevicePushToken)
            {
                // If we have a device token, send up the new push token,
                // otherwise it should happen in POST /device.
                if (_appSettings.DeviceToken != null)
                {
                    var result = await this.UpdateDeviceAsync(pushToken);

                    if (result)
                    {
                        _appSettings.DevicePushToken = pushToken;
                        _appSettings.Save();
                    }
                }
            }
        }

        internal class DeviceRegistration
        {
            public string AccessToken { get; set; }
            public string ServiceRoot { get; set; }
            public string ServerSignature { get; set; }
        }

        private AsyncLock getAccessTokenAsyncLock = new AsyncLock();
        internal async Task<string> GetAccessToken()
        {
            using (await getAccessTokenAsyncLock.LockAsync())
            {
                if (_appSettings.UserToken != null)
                {
                    return _appSettings.UserToken;
                }
                else if (_appSettings.DeviceToken != null)
                {
                    return _appSettings.DeviceToken;
                }

                _appSettings.DeviceToken = await GetDeviceToken().ConfigureAwait(false);
                _appSettings.Save();
                return _appSettings.DeviceToken;
            }
        }

        private string GenerateServerSig()
        {
            string stringToSign = string.Format("{0}\n", AppKey);
            return PlatformAccess.Current.SignString(sharedSecret,stringToSign);
        }

        private async Task<string> GetDeviceToken()
        {
            var pushToken = PlatformAccess.Current.GetPushToken();

            var reg = GenericRestCall<DeviceRegistration>(PostVerb, "/devices", new
                {
                    AppId = AppId,
                    AppKey = AppKey,
                    ApplicationId = PlatformAccess.Current.ApplicationID,
                    Platform = PlatformAccess.Current.Platform,
                    UniqueID = PlatformAccess.Current.DeviceUniqueId,
                    Model = await PlatformAccess.Current.Model,
                    OSVersion = await PlatformAccess.Current.OSVersion,
                    PushToken = pushToken,
                    AppVersion = _appSettings.Options.AppVersion ?? PlatformAccess.Current.AppVersion,
                    Tag = _appSettings.Options.DeviceTag
                }, false, true);

            var dr = await ResultConversionHelper<DeviceRegistration, DeviceRegistration> (
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

                        // POST doesn't accept push tokens, so send it here
                        this.PushTokenChanged(this, EventArgs.Empty);
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
                return Task.FromResult (_user);
            }
            else if (_user != null && !reload) {
                return Task.FromResult (_user);
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
                PlatformAccess.Current.InvokeOnUiThread (async () => {

                    var r = false;
                    if (await OnServiceException (this, buddyException)) {
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
                        BuddyResult<T2> newResult = r2.IsFaulted ? new BuddyResult<T2>() { Error = new BuddyNoInternetException(null) } : r2.Result;

                        completed(r1Result, newResult); tcs.SetResult(newResult);
                    }
                });
            return tcs.Task;
        }
     
        internal void ClearCredentials(bool clearUser = true, bool clearDevice = true) {

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

        private readonly AsyncLock getServiceAsyncLock = new AsyncLock();
        protected virtual async Task<IRemoteMethodProvider> GetService()
        {
            using (await getServiceAsyncLock.LockAsync())
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

        private async Task CheckConnectivity(int retryCount = 1)
        {
            var r = await GetAsync<string>("/service/ping", null);

            if (r != null && r.IsSuccess)
            {
                await OnConnectivityChanged(PlatformAccess.Current.ConnectionType);
            }
            else
            {
                var waitTimeInMilliseconds = getNextWaitTime(retryCount);

                DotNetDeltas.Sleep(waitTimeInMilliseconds);

                await CheckConnectivity(++retryCount);
            }
        }

        private const int retryCapInMilliseconds = 30 * 1000;
        private const int retryBaseInMilliseconds = 500;

        private int getNextWaitTime(int retryCount)
        {
            // http://www.awsarchitectureblog.com/2015/03/backoff.html
            var waitTimeInMilliseconds = new Random().Next((int)Math.Min(retryCapInMilliseconds, retryBaseInMilliseconds *
                                                    Math.Pow(2, Math.Min(retryCount, 32))));

            return waitTimeInMilliseconds;
        }

        private readonly AsyncLock onConnectivityChangedAsyncLock = new AsyncLock();
        protected virtual async Task OnConnectivityChanged(ConnectivityLevel level)
        {
            using (await onConnectivityChangedAsyncLock.LockAsync())
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
                        await CheckConnectivity();
                        break;
                }
            }
        }

        protected async Task<bool> OnServiceException(BuddyClient client, BuddyServiceException buddyException)
        {
            if (buddyException is BuddyUnauthorizedException)
            {
                client.OnAuthorizationFailure((BuddyUnauthorizedException)buddyException);
                return false;
            }
            else if (buddyException is BuddyNoInternetException)
            {
                await client.OnConnectivityChanged(ConnectivityLevel.None);
                return false;
            }

            bool result = false;

            if (ServiceException != null)
            {
                var args = new ServiceExceptionEventArgs(buddyException);
                ServiceException(this, args);
                result = args.ShouldThrow;
            }
            return result;
        }

        private int _processingAuthFailure = 0;
        internal virtual void OnAuthorizationFailure(BuddyUnauthorizedException exception)
        {
            var count = Interlocked.Increment(ref _processingAuthFailure);

            if (count > 1)
            {
                return;
            }

            bool showLoginDialog = exception == null, exceptionCaught = false;

            try
            {
                if (exception != null)
                {
                    switch (exception.Error)
                    {
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

                if (showLoginDialog && this.AuthorizationNeedsUserLogin != null)
                {
                    PlatformAccess.Current.InvokeOnUiThread(() =>
                    {
                        this.AuthorizationNeedsUserLogin(this, new EventArgs());

                        Interlocked.Exchange(ref _processingAuthFailure, 0);
                    });
                }
            }
            catch
            {
                exceptionCaught = true;

                throw;
            }
            finally
            {
                if (exceptionCaught || !showLoginDialog || this.AuthorizationNeedsUserLogin == null)
                {
                    Interlocked.Exchange(ref _processingAuthFailure, 0);
                }
            }
        }

        protected virtual void OnAuthLevelChanged()
        {
            PlatformAccess.Current.InvokeOnUiThread(() =>
            {
                if (this.AuthorizationLevelChanged != null)
                {
                    this.AuthorizationLevelChanged(this, EventArgs.Empty);
                }
            });
        }

        private void UpdateAccessLevel()
        {
            var old = AuthLevel;
            AuthenticationLevel authLevel = AuthenticationLevel.None;
            if (_appSettings.DeviceToken != null) authLevel = AuthenticationLevel.Device;
            if (_appSettings.UserToken != null) authLevel = AuthenticationLevel.User;
            AuthLevel = authLevel;

            if (old != authLevel)
            {
                OnAuthLevelChanged();
            }
        }

        // User auth.
        public async System.Threading.Tasks.Task<BuddyResult<User>> CreateUserAsync(
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

            return await LoginUserCoreAsync<User>("/users",  new
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

        private async Task<BuddyResult<T>> LoginUserCoreAsync<T>(string path, object parameters) where T : User
        {
            var t = await PostAsync<IDictionary<string,object>>(
                    path,
                    parameters);

                // update our current user
                //
                T user = null;
                if(t.IsSuccess && t.Value != null) {

                    // create the user
                    //
                    var dict = t.Value;
                    user = FromDictionary<T>(dict);
                    SetCurrentUser(user, (string)dict["accessToken"], (DateTime?) dict["accessTokenExpires"]);

                }
                return t.Convert<T>((d) => {
                    return user;
                });
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

        private async Task<BuddyResult<T>> GenericRestCall<T>(string verb, string path, object parameters, bool allowThrow, bool skipAuth = false)
        {
            var service = await GetService().ConfigureAwait(false);

            var callResult = await service.CallMethodAsync<T>(
                verb,
                path,
                AddLocationToParameters(parameters), skipAuth).ConfigureAwait(false);

            var procResult = await HandleServiceResult(callResult, allowThrow).ConfigureAwait(false);

            return procResult;
        }

        public async Task<BuddyResult<T>> GetAsync<T>(string path, object parameters = null)
        {
            return await GenericRestCall<T>(GetVerb, path, parameters, false);
        }

        public async Task<BuddyResult<T>> PostAsync<T>(string path, object parameters = null)
        {
            return await GenericRestCall<T>(PostVerb, path, parameters, false);
        }

        public async Task<BuddyResult<T>> PatchAsync<T>(string path, object parameters = null)
        {
            return await GenericRestCall<T>(PatchVerb, path, parameters, false);
        }

        public async Task<BuddyResult<T>> PutAsync<T>(string path, object parameters = null)
        {
            return await GenericRestCall<T>(PutVerb, path, parameters, false);
        }

        public async Task<BuddyResult<T>> DeleteAsync<T>(string path, object parameters = null)
        {
            return await GenericRestCall<T>(DeleteVerb, path, parameters, false);
        }
        #endregion
    }

    internal enum AuthenticationLevel {
        None = 0,
        Device,
        User
    }
}
