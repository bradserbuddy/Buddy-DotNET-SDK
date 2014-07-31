﻿using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace BuddySDK
{
    public abstract class BuddyBase: System.ComponentModel.INotifyPropertyChanged
    {
        private static Dictionary<Type, List<Tuple<string, string>>> _propMappings = new Dictionary<Type, List<Tuple<string, string>>>();

        protected static List<Tuple<string, string>> EnsureMappings(object t)
        {
            if (_propMappings.ContainsKey(t.GetType()))
            {
                return _propMappings[t.GetType()];
            }
            var l = new List<Tuple<string, string>>();
            foreach (var prop in t.GetType().GetProperties())
            {
                string jsonName = prop.Name;

                var attr = prop.GetCustomAttribute<Newtonsoft.Json.JsonPropertyAttribute>();

                if (attr != null && attr.PropertyName != null)
                {
                    jsonName = attr.PropertyName;
                }

                l.Add(new Tuple<string, string>(prop.Name, jsonName));
            }
            _propMappings[t.GetType()] = l;
            return l;
        }

        protected bool IsDeleted
        {
            get;
            private set;
        }

        public bool IsPopulated {
            get;
            private set;
        }

        protected string MetadataID
        {
            get
            {
                return this.ID;
            }
        }

        [JsonProperty("id")]
        public string ID {
            get
            {
                return GetValueOrDefault<string>("id");
            }
        }

        [JsonProperty("location")]
        public virtual BuddyGeoLocation Location
        {
            get
            {
                return GetValueOrDefault<BuddyGeoLocation>("Location");
            }
            set
            {
                SetValue<BuddyGeoLocation>("Location", value, checkIsProp:false);
            }
        }

        [JsonProperty("readPermissions")]
        public BuddyPermissions ReadPermissions
        {
            get
            {
                return GetValueOrDefault<BuddyPermissions>("ReadPermissions");
            }
            set
            {
                SetValue<BuddyPermissions>("ReadPermissions", value, checkIsProp: false);
            }
        }

        [JsonProperty("writePermissions")]
        public BuddyPermissions WritePermissions
        {
            get
            {
                return GetValueOrDefault<BuddyPermissions>("WritePermissions");
            }
            set
            {
                SetValue<BuddyPermissions>("WritePermissions", value, checkIsProp: false);
            }
        }

        [JsonProperty("created")]
        public DateTime Created {
            get {
                return GetValueOrDefault<DateTime>("Created");
            }
            set
            {
                SetValue<DateTime>("Created", value, checkIsProp:false);
            }
        }

        [JsonProperty("lastModified")]
        public DateTime LastModified {
            get {
                return GetValueOrDefault<DateTime>("LastModified");
            }
            set
            {
                SetValue<DateTime>("LastModified", value, checkIsProp:false);
            }
        }

        [JsonProperty("tag")]
        public string Tag { 

            get {
                return GetValueOrDefault<string>("Tag");
            }
            set
            {
                SetValue<string>("Tag", value, checkIsProp:false);
            }
        }

        protected virtual string Path
        {
            get
            {
                var attr = this.GetType().GetCustomAttribute<BuddyObjectPathAttribute>();
                if (attr == null)
                {
                    throw new NotImplementedException("BuddyObjectPathAttribute required");
                }
                return attr.Path;
            }
        }


        protected BuddyBase()
        {
            EnsureMappings(this);
        }

        protected BuddyBase(string id)
        {
            EnsureMappings(this);

            if (id != null)
            {
                SetValue<string>("ID", id);
            }
        }

        protected virtual void EnsureValid()
        {
            if (IsDeleted)
            {
                throw new ObjectDisposedException("This object has been deleted.");
            }
        }

        protected virtual string GetObjectPath()
        {
            if (ID == null) throw new InvalidOperationException("ID required.");

            return String.Format(CultureInfo.InvariantCulture, "{0}/{1}", Path, ID);
        }

        public bool IsDirty
        {
            get
            {
                return (from t in _values.Values where t.IsDirty select t).Any();
            }
        }

        private class ValueEntry
        {
            public object Value { get; set; }
            public bool IsDirty { get; set; }

            public ValueEntry(object value, bool isDirty = false)
            {
                Value = value;
                IsDirty = isDirty;
            }
        }

        private IDictionary<string, ValueEntry> _values = new Dictionary<string, ValueEntry>(DotNetDeltas.InvariantComparer(true));

        protected T GetValueOrDefault<T>(string key, T defaultValue = default(T))
        {
            EnsureValid();
            ValueEntry  v;
            if (_values.TryGetValue(key, out v))
            {
                return (T)ChangeType<T>(v.Value);
            }

            return defaultValue;
        }

        private object ChangeType<T>(object value)
        {

            if (typeof(T).IsInstanceOfType(value)) {
                return (T)value;
            }

			var enumType = GetEnumType<T> ();

			if (enumType != null)
            {                  
                if (value is string)
                {
					// Enum.Parse doesn't handle null values
					if (value == null) {
						return default(T);
					}

					try // needed because Enum.IsDefined is case-sensitive, and passing in a non-enum string causes Enum.Parse to throw
                    {
						return Enum.Parse(enumType, (string)value, true);
                    }
                    catch
                    {
                    }
                }
                else
                {
                    if (value is long)
                    {
                        value = Convert.ToInt32(value);
                    }

					if (Enum.IsDefined(enumType, value))
                    {
						return Enum.ToObject(enumType, value);
                    }
                }
            }

			// Convert.ChangeType doesn't handle null values
			if (GetIsNullable<T>() && value == null) {
				return default(T);
			}

			// Convert.ChangeType doesn't handle nullable types
			return Convert.ChangeType(value, GetNonNullableType<T> ());
        }

		private Type GetEnumType<T>()
		{
			Type type = GetNonNullableType<T>();
#if NETFX_CORE
            return System.Reflection.IntrospectionExtensions.GetTypeInfo(type).IsEnum ? type : null;
#else
            return type.IsEnum ? type : null;
#endif
		}
		
		private Type GetNonNullableType<T>()
		{
			return GetIsNullable<T>() ? Nullable.GetUnderlyingType(typeof(T)) : typeof(T);
		}

		private bool GetIsNullable<T>()
		{
            
#if NETFX_CORE
            var t = System.Reflection.IntrospectionExtensions.GetTypeInfo(typeof(T));
#else
            var t = typeof(T);
#endif
			return t.IsGenericType && t.GetGenericTypeDefinition () == typeof(Nullable<>);
		}

		protected virtual void SetValueCore<T>(string key, T value)
        {
            if (key == "Location" && !(value is BuddyGeoLocation))
            {
				value = (T)(object)BuddyGeoLocation.Parse (value);
            }
            else if (key == "Website" && !(value is Uri))
            {
                value = (T)(object)new Uri((string)(object)value);
            }

            _values[key] = new ValueEntry(value, true);
        }

        protected void SetValue<T>(string key, T value, bool notify = true, bool checkIsProp = true)
        {
            EnsureValid();
            if (checkIsProp && this.GetType().GetProperty(key) == null)
            {
                throw new ArgumentException(String.Format("{0} is not a property on {1}", key, GetType().Name));
            }

            var oldValue = GetValueOrDefault<T>(key);
                if (Object.Equals(value, oldValue))
                {
                    return;
                }
            SetValueCore<T>(key, value);

            if (notify)
            {
                OnPropertyChanged(key);
            }
        }

        internal void UpdateFrom(BuddyBase other)
        {
            EnsureValid();
            if (this.GetType().IsInstanceOfType(other))
            {
                throw new ArgumentException();
            }

            var d = new Dictionary<string, object>(DotNetDeltas.InvariantComparer(true));
            foreach (var kvp in other._values)
            {
                d[kvp.Key] = kvp.Value.Value;
            }
            Update(d);
        }

        internal void Update(IDictionary<string, object> values)
        {
            IsPopulated = true;
            var mappings = EnsureMappings (this);

            foreach (var kvp in values)
            {
                string name = mappings.Where(m => m.Item2 == kvp.Key).Select(i => i.Item1).FirstOrDefault();

                SetValue(name ?? kvp.Key, kvp.Value, false, false);
            }
            foreach (var kvp in _values)
            {
                kvp.Value.IsDirty = false;
            }
            OnPropertyChanged (null);
        }

        #region IPropertyNotifyChangedStuff

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string prop) {
            if (PropertyChanged != null)
            {
                PlatformAccess.Current.InvokeOnUiThread (() => {
                    PropertyChanged (this, new System.ComponentModel.PropertyChangedEventArgs (prop));
                });
            }
         }


        #endregion

    }

    // From http://www.hanselman.com/blog/ComparingTwoTechniquesInNETAsynchronousCoordinationPrimitives.aspx
    public sealed class AsyncLock
    {
        private readonly SemaphoreSlim m_semaphore = new SemaphoreSlim(1, 1);
        private readonly Task<IDisposable> m_releaser;

        public AsyncLock()
        {
            m_releaser = Task.FromResult((IDisposable)new Releaser(this));
        }

        public Task<IDisposable> LockAsync()
        {
            var wait = m_semaphore.WaitAsync();
            return wait.IsCompleted ?
                        m_releaser :
                        wait.ContinueWith((_, state) => (IDisposable)state,
                            m_releaser.Result, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private sealed class Releaser : IDisposable
        {
            private readonly AsyncLock m_toRelease;
            internal Releaser(AsyncLock toRelease) { m_toRelease = toRelease; }
            public void Dispose() { m_toRelease.m_semaphore.Release(); }
        }
    }
}