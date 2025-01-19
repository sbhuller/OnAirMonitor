using System;
using Microsoft.Win32;

public class CameraUsageChecker
{
    // Returns true if the camera is currently in use based on LastUsedTimeStop == 0.
    public static bool IsCameraInUse()
    {
        // Registry path where webcam usage is recorded
        const string registryPath = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam\NonPackaged";

        // Open the subkey under HKEY_CURRENT_USER
        using (RegistryKey rootKey = Registry.CurrentUser.OpenSubKey(registryPath))
        {
            // If the key doesn't exist or has no subkeys, assume camera is not in use
            if (rootKey == null)
                return false;

            // Iterate through each subkey (one subkey per app/executable)
            foreach (string subKeyName in rootKey.GetSubKeyNames())
            {
                using (RegistryKey appKey = rootKey.OpenSubKey(subKeyName))
                {
                    if (appKey != null)
                    {
                        // Retrieve the LastUsedTimeStop value (QWORD in registry)
                        object value = appKey.GetValue("LastUsedTimeStop");
                        if (value is long lastUsedTimeStop)
                        {
                            // If LastUsedTimeStop is 0, Windows indicates the camera is still in use
                            if (lastUsedTimeStop == 0)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
        }

        // If no subkeys show LastUsedTimeStop == 0, camera is not in use
        return false;
    }


}
