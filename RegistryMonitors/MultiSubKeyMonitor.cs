using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace RegistryMonitors
{
    public class MultiSubKeyMonitor : IDisposable
    {
        private readonly List<SingleKeyMonitor> _monitors = new();
        private bool _initialized = false;

        private const string LAST_USED_VALUE_NAME = "LastUsedTimeStop";

        #region Win32 P/Invoke for enumeration

        private static readonly IntPtr HKEY_CURRENT_USER = new IntPtr(unchecked((int)0x80000001));
        private const int KEY_READ = 0x20019; // Includes KEY_ENUMERATE_SUB_KEYS & KEY_QUERY_VALUE
        private const int ERROR_SUCCESS = 0;
        private const int ERROR_NO_MORE_ITEMS = 259;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegOpenKeyEx(
            IntPtr hKey,
            string lpSubKey,
            int ulOptions,
            int samDesired,
            out IntPtr phkResult);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int RegEnumKeyEx(
            IntPtr hKey,
            uint dwIndex,
            System.Text.StringBuilder lpName,
            ref uint lpcName,
            IntPtr lpReserved,
            IntPtr lpClass,
            IntPtr lpcClass,
            out long lpftLastWriteTime);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegCloseKey(IntPtr hKey);

        #endregion

        // This event is fired when ANY subkey's LastUsedTimeStamp changes
        public event EventHandler<(string subKeyName, long? newValue)> SubKeyValueChanged;

        private string _parentPath;

        public MultiSubKeyMonitor(string parentPath)
        {
            _parentPath = parentPath;
        }

        public void InitializeAndStart()
        {
            if (_initialized) return;

            // 1. Open the parent key
            int openResult = RegOpenKeyEx(
                HKEY_CURRENT_USER,
                _parentPath,
                0,
                KEY_READ,
                out IntPtr parentHandle);

            if (openResult != ERROR_SUCCESS)
            {
                throw new ApplicationException(
                    $"Failed to open parent key: {_parentPath}, Error={openResult}");
            }

            // 2. Enumerate subkeys
            try
            {
                EnumerateSubKeysAndCreateMonitors(parentHandle);
            }
            finally
            {
                // We can close the parent key right after enumeration,
                // because each SingleKeyMonitor will open its own handle
                RegCloseKey(parentHandle);
            }

            _initialized = true;
        }

        private void EnumerateSubKeysAndCreateMonitors(IntPtr parentHandle)
        {
            uint index = 0;
            while (true)
            {
                const int MAX_KEY_NAME_LEN = 255;
                var nameBuffer = new System.Text.StringBuilder(MAX_KEY_NAME_LEN + 1);
                uint nameLength = (uint)nameBuffer.Capacity;
                long lastWriteTime;

                int enumResult = RegEnumKeyEx(
                    parentHandle,
                    index,
                    nameBuffer,
                    ref nameLength,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    out lastWriteTime);

                if (enumResult == ERROR_NO_MORE_ITEMS)
                {
                    // done
                    break;
                }
                else if (enumResult != ERROR_SUCCESS)
                {
                    // Some error
                    throw new ApplicationException($"RegEnumKeyEx failed. Error={enumResult}");
                }

                string subKeyName = nameBuffer.ToString();

                // Full subkey path: "ParentPath\subKeyName"
                string fullSubKeyPath = _parentPath + "\\" + subKeyName;

                // Create a monitor for this subkey
                var singleKeyMonitor = new SingleKeyMonitor(fullSubKeyPath, LAST_USED_VALUE_NAME);
                singleKeyMonitor.ValueChanged += (s, newVal) =>
                {
                    // Fire a consolidated event that includes which subkey changed
                    OnSubKeyValueChanged(subKeyName, newVal);
                };

                // Start monitoring
                singleKeyMonitor.Start();
                _monitors.Add(singleKeyMonitor);

                index++;
            }
        }

        private void OnSubKeyValueChanged(string subKeyName, long? newValue)
        {
            SubKeyValueChanged?.Invoke(this, (subKeyName, newValue));
        }

        public void StopAll()
        {
            foreach (var m in _monitors)
            {
                m.Stop();
            }
            _monitors.Clear();
        }

        public void Dispose()
        {
            StopAll();
        }
    }
}
