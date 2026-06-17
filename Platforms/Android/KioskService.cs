using Android.App;
using Android.App.Admin;
using Android.Content;
using Android.OS;

namespace TaroziAPP.Platforms.Android
{
    /// <summary>
    /// Service for managing kiosk mode (Device Owner and Home App functionality)
    /// </summary>
    public static class KioskService
    {
        public static bool IsKioskActive(Activity activity)
        {
            var devicePolicyManager = (DevicePolicyManager)activity.GetSystemService(Context.DevicePolicyService);
            var deviceAdmin = new ComponentName(activity, Java.Lang.Class.FromType(typeof(DeviceAdminReceiver)));
            
            return devicePolicyManager.IsDeviceOwnerApp(activity.PackageName);
        }

        public static void EnterKioskMode(Activity activity)
        {
            if (IsKioskActive(activity))
            {
                var devicePolicyManager = (DevicePolicyManager)activity.GetSystemService(Context.DevicePolicyService);
                var deviceAdmin = new ComponentName(activity, Java.Lang.Class.FromType(typeof(DeviceAdminReceiver)));

                // Set app as lock task (kiosk mode)
                var packages = new string[] { activity.PackageName };
                devicePolicyManager.SetLockTaskPackages(deviceAdmin, packages);

                // Start lock task mode
                activity.StartLockTask();
            }
        }

        public static void ExitKioskMode(Activity activity)
        {
            if (IsKioskActive(activity))
            {
                var devicePolicyManager = (DevicePolicyManager)activity.GetSystemService(Context.DevicePolicyService);
                var deviceAdmin = new ComponentName(activity, Java.Lang.Class.FromType(typeof(DeviceAdminReceiver)));

                // Stop lock task mode
                activity.StopLockTask();

                // Clear lock task packages (remove app from lock task list)
                var emptyPackages = new string[] { };
                devicePolicyManager.SetLockTaskPackages(deviceAdmin, emptyPackages);

                // Remove persistent preferred activity (home app)
                var intentFilters = new IntentFilter[]
                {
                    new IntentFilter(Intent.ActionMain)
                };
                intentFilters[0].AddCategory(Intent.CategoryHome);
                intentFilters[0].AddCategory(Intent.CategoryDefault);

                var activities = new ComponentName[]
                {
                    new ComponentName(activity, Java.Lang.Class.FromType(typeof(MainActivity)))
                };

                devicePolicyManager.ClearPackagePersistentPreferredActivities(deviceAdmin, activity.PackageName);
            }
            else
            {
                // Even if not device owner, try to stop lock task
                activity.StopLockTask();
            }
        }

        public static void SetAsHomeApp(Activity activity)
        {
            if (IsKioskActive(activity))
            {
                var devicePolicyManager = (DevicePolicyManager)activity.GetSystemService(Context.DevicePolicyService);
                var deviceAdmin = new ComponentName(activity, Java.Lang.Class.FromType(typeof(DeviceAdminReceiver)));

                // Set preferred activities for home intent
                var intentFilters = new IntentFilter[]
                {
                    new IntentFilter(Intent.ActionMain)
                };
                intentFilters[0].AddCategory(Intent.CategoryHome);
                intentFilters[0].AddCategory(Intent.CategoryDefault);

                var activities = new ComponentName[]
                {
                    new ComponentName(activity, Java.Lang.Class.FromType(typeof(MainActivity)))
                };

                devicePolicyManager.AddPersistentPreferredActivity(
                    deviceAdmin,
                    intentFilters[0],
                    activities[0]
                );
            }
        }
    }

    /// <summary>
    /// Device Admin Receiver for Device Owner functionality
    /// </summary>
    [BroadcastReceiver(
        Name = "TaroziAPP.Platforms.Android.DeviceAdminReceiver",
        Permission = "android.permission.BIND_DEVICE_ADMIN",
        Exported = true)]
    [MetaData("android.app.device_admin", Resource = "@xml/device_admin")]
    [IntentFilter(new[] { "android.app.action.DEVICE_ADMIN_ENABLED" })]
    public class DeviceAdminReceiver : global::Android.App.Admin.DeviceAdminReceiver
    {
        public override void OnEnabled(Context context, Intent intent)
        {
            base.OnEnabled(context, intent);
            System.Diagnostics.Debug.WriteLine("Device Admin Enabled");
        }

        public override void OnDisabled(Context context, Intent intent)
        {
            base.OnDisabled(context, intent);
            System.Diagnostics.Debug.WriteLine("Device Admin Disabled");
        }
    }
}

