using Android.Content;
using Android.Content.PM;

namespace CNCO.YccuSdk.Platform.Droid;

internal static class AndroidPermissions
{
    public static bool IsGranted(Context context, string permission) =>
        context.CheckSelfPermission(permission) == Permission.Granted;

    public static void AssertGranted(Context context, string permission)
    {
        if (!IsGranted(context, permission)) throw new PermissionsException(permission);
    }
}
