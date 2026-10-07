using System;
using UnityEngine;
using UnityEngine.Android;

namespace Automatic.Game
{
    /// <summary>
    /// The few Android calls the hot code needs, as raw JNI (no AndroidJavaObject.Call&lt;T&gt;, which
    /// would need AOT generic instantiations the shell may not have). Android only.
    /// </summary>
    public static class Jni
    {
        private static readonly jvalue[] None = Array.Empty<jvalue>();

        private static IntPtr Activity => AndroidApplication.currentActivity.GetRawObject();

        /// <summary>A string extra of the intent that launched the app, or null.</summary>
        public static string IntentString(string key)
        {
            var intent = Call(Activity, "getIntent", "()Landroid/content/Intent;");
            var name = AndroidJNI.NewStringUTF(key);
            try
            {
                return AndroidJNI.CallStringMethod(intent, Method(intent, "getStringExtra", "(Ljava/lang/String;)Ljava/lang/String;"), new[] { new jvalue { l = name } });
            }
            finally
            {
                AndroidJNI.DeleteLocalRef(name);
                AndroidJNI.DeleteLocalRef(intent);
            }
        }

        /// <summary>Battery temperature in °C (the sticky ACTION_BATTERY_CHANGED broadcast), NaN if unknown.</summary>
        public static float BatteryCelsius()
        {
            var filterClass = AndroidJNI.FindClass("android/content/IntentFilter");
            var action = AndroidJNI.NewStringUTF("android.intent.action.BATTERY_CHANGED");
            var filter = AndroidJNI.NewObject(filterClass, AndroidJNI.GetMethodID(filterClass, "<init>", "(Ljava/lang/String;)V"), new[] { new jvalue { l = action } });
            var battery = AndroidJNI.CallObjectMethod(Activity,
                Method(Activity, "registerReceiver", "(Landroid/content/BroadcastReceiver;Landroid/content/IntentFilter;)Landroid/content/Intent;"),
                new[] { new jvalue { l = IntPtr.Zero }, new jvalue { l = filter } });
            var key = AndroidJNI.NewStringUTF("temperature");
            try
            {
                if (battery == IntPtr.Zero) return float.NaN;
                var tenths = AndroidJNI.CallIntMethod(battery, Method(battery, "getIntExtra", "(Ljava/lang/String;I)I"), new[] { new jvalue { l = key }, new jvalue { i = int.MinValue } });
                return tenths == int.MinValue ? float.NaN : tenths / 10f;
            }
            finally
            {
                foreach (var r in new[] { key, battery, filter, action, filterClass })
                    if (r != IntPtr.Zero) AndroidJNI.DeleteLocalRef(r);
            }
        }

        /// <summary>
        /// PowerManager thermal status (0 none, 1 light, 2 moderate, 3 severe, 4 critical; API 29)
        /// and headroom (1.0 = the device starts throttling; NaN when unsupported or asked within 1 s; API 30).
        /// </summary>
        public static (int Status, float Headroom) Thermal()
        {
            var service = AndroidJNI.NewStringUTF("power");
            var power = AndroidJNI.CallObjectMethod(Activity, Method(Activity, "getSystemService", "(Ljava/lang/String;)Ljava/lang/Object;"), new[] { new jvalue { l = service } });
            try
            {
                var status = AndroidJNI.CallIntMethod(power, Method(power, "getCurrentThermalStatus", "()I"), None);
                var headroom = AndroidJNI.CallFloatMethod(power, Method(power, "getThermalHeadroom", "(I)F"), new[] { new jvalue { i = 0 } });
                return (status, headroom);
            }
            finally
            {
                AndroidJNI.DeleteLocalRef(power);
                AndroidJNI.DeleteLocalRef(service);
            }
        }

        private static IntPtr Call(IntPtr obj, string name, string signature) =>
            AndroidJNI.CallObjectMethod(obj, Method(obj, name, signature), None);

        private static IntPtr Method(IntPtr obj, string name, string signature)
        {
            var cls = AndroidJNI.GetObjectClass(obj);
            try
            {
                var id = AndroidJNI.GetMethodID(cls, name, signature);
                if (id != IntPtr.Zero) return id;
                // Missing on this API level: clear the pending NoSuchMethodError before the next JNI call.
                AndroidJNI.ExceptionClear();
                throw new MissingMethodException(name + signature);
            }
            finally
            {
                AndroidJNI.DeleteLocalRef(cls);
            }
        }
    }
}
